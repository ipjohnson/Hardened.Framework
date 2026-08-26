using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.Web.Runtime.Health;

/// <summary>
/// Serves liveness and readiness at fixed paths.
/// </summary>
/// <remarks>
/// <para>
/// Registered as one more <c>IWebExecutionRequestHandlerProvider</c>, which is how routing tables
/// themselves are registered and how <c>OpenApiDocumentProvider</c> serves its document - so this
/// needs no route attribute and no entry in any generated table. Providers are consulted in reverse
/// registration order, so an application that declares its own <c>/health/ready</c> wins.
/// </para>
/// <para>
/// <b>What this bypasses, exactly.</b> <c>WebExecutionHandlerService</c> is itself the last
/// middleware, so everything registered through <c>IMiddlewareService</c> - CORS, a global rate
/// limiter - still runs before a probe reaches here. What is skipped is the per-handler filter set
/// from <c>IGlobalFilterRegistry</c>, because this builds its own chain. That is the intended split:
/// a probe must not need a credential, and with default-deny authorization there is no attribute
/// here to hang an exemption on.
/// </para>
/// <para>
/// <b>Not for Lambda.</b> There is no pool to drain and no orchestrator polling, so nothing should
/// register this in a Lambda module.
/// </para>
/// </remarks>
public class HealthCheckProvider : IWebExecutionRequestHandlerProvider {
    private readonly HealthCheckConfiguration _config;
    private readonly IServiceProvider _rootProvider;

    private IExecutionRequestHandler? _live;
    private IExecutionRequestHandler? _ready;

    public HealthCheckProvider(HealthCheckConfiguration config, IServiceProvider rootProvider) {
        _config = config;
        _rootProvider = rootProvider;
    }

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        var method = context.Request.Method;

        // HEAD as well as GET: a probe issuing HEAD is asking the same question, and the routing
        // table's usual HEAD-to-GET redirection does not apply to a provider serving its own chain.
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var path = context.Request.Path;

        // Built once, lazily, rather than per request - conventions are asked per handler
        // construction, which is the contract they are written against.
        if (string.Equals(path, _config.LivePath, StringComparison.Ordinal)) {
            return new RequestHandlerInfo(
                _live ??= Handler.For(
                    _rootProvider, _config, _config.LivePath,
                    nameof(HealthCheckController.Live),
                    static (controller, context) => controller.Live(context)),
                PathTokenCollection.Empty);
        }

        if (string.Equals(path, _config.ReadyPath, StringComparison.Ordinal)) {
            return new RequestHandlerInfo(
                _ready ??= Handler.For(
                    _rootProvider, _config, _config.ReadyPath,
                    nameof(HealthCheckController.Ready),
                    static (controller, context) => controller.Ready(context)),
                PathTokenCollection.Empty);
        }

        return null;
    }

    /// <summary>
    /// A probe, run as an ordinary handler.
    /// </summary>
    private sealed class Handler : BaseExecutionHandler<HealthCheckController> {

        /// <summary>
        /// Empty, and load bearing. There is deliberately no <c>[AllowAnonymous]</c>: that is the one
        /// thing a convention cannot narrow, and without it a probe inherits the application's
        /// posture rather than overriding it.
        /// </summary>
        private static readonly object[] Metadata = [];

        private Handler(ExecutionHandlerSetup setup) : base(setup) { }

        public static Handler For(
            IServiceProvider serviceProvider,
            HealthCheckConfiguration config,
            string path,
            string methodName,
            Func<HealthCheckController, IExecutionContext, Task> answer) =>
            new(ExecutionHelper.AsyncStandardFilterEmptyParameters<HealthCheckController>(
                serviceProvider,
                new ExecutionRequestHandlerInfo(
                    path, "GET", typeof(HealthCheckController), methodName, [], Metadata,
                    config.Requirement),
                (context, controller) => answer(controller, context),
                ExecutionHelper.GetFilterInfo(Metadata)));
    }
}
