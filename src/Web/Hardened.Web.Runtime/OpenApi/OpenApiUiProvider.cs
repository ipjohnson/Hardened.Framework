using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Serves one reference page, at the path its configuration names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its chain is built by <see cref="ExecutionHelper"/>, and that is the point.</b> Authorization
/// conventions are applied in <c>ExecutionHelper.CreateFilterArray</c>, ahead of the global filter
/// registry - so a provider that hand-rolls an <c>ExecutionChain</c>, which is what
/// <c>HealthCheckProvider</c> and <see cref="OpenApiDocumentProvider"/> both do, is invisible to an
/// <c>IAuthorizationConvention</c>. Going through the same helper the generated handlers go through
/// is what makes this page gate-able by convention despite having no generated route.
/// </para>
/// <para>
/// It has no generated route because the path is configuration: an application may publish several
/// specifications and serve a page for each. A route path is a compile-time constant read from an
/// attribute, so a page whose path is chosen by the application that installs it cannot be one.
/// </para>
/// <para>
/// The handler is built once, lazily, rather than per request - conventions are asked per handler
/// construction, which is the contract they are written against.
/// </para>
/// </remarks>
public class OpenApiUiProvider : IWebExecutionRequestHandlerProvider {
    private readonly IOpenApiUiConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    private IExecutionRequestHandler? _handler;

    public OpenApiUiProvider(IOpenApiUiConfiguration configuration, IServiceProvider serviceProvider) {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// The page this provider serves. Exposed because it is not in the container - several pages may
    /// be installed, so each holds its own - and there is otherwise no way to ask an application what
    /// it is publishing.
    /// </summary>
    public IOpenApiUiConfiguration Configuration => _configuration;

    /// <summary>What a request to this path may do, when it did something else.</summary>
    private const string Allow = "GET, HEAD";

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        if (!string.Equals(context.Request.Path, _configuration.Path, StringComparison.Ordinal)) {
            return null;
        }

        // HEAD as well as GET. WebExecutionHandlerService.Dispatch drops the body and reports the
        // length for one, so accepting it here is all that is needed.
        var method = context.Request.Method;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            // The path is checked first so a write to it answers 405 rather than 404. The resource
            // exists; the verb is the problem, and API Gateway and CloudFront cache the two
            // differently.
            return RequestHandlerInfo.MethodNotAllowed(Allow);
        }

        return new RequestHandlerInfo(
            _handler ??= new Handler(_configuration, _serviceProvider), PathTokenCollection.Empty);
    }

    private sealed class Handler : BaseExecutionHandler<OpenApiUiController> {

        /// <remarks>
        /// The same assignment the generator emits beside every handler declaring an
        /// <c>[Output&lt;T&gt;]</c>. It is what makes "this output's model matches what the handler
        /// returns" a compile error rather than a runtime cast - there is no generator to emit it
        /// here, so it is written out.
        /// </remarks>
        private static readonly IHardenedResponseOutput<OpenApiUiModel> OutputCheck =
            new OpenApiUiPage();

        private static readonly Func<IExecutionContext, IHardenedResponseOutput> OutputFactory =
            static _ => new OpenApiUiPage();

        private static readonly object[] Metadata = [new OutputAttribute<OpenApiUiPage>()];

        public Handler(IOpenApiUiConfiguration configuration, IServiceProvider serviceProvider)
            : base(ExecutionHelper.StandardFilterEmptyParameters<OpenApiUiController>(
                serviceProvider,
                new ExecutionRequestHandlerInfo(
                    configuration.Path, "GET", typeof(OpenApiUiController),
                    nameof(OpenApiUiController.Index), [], Metadata),
                // A lambda rather than a static method, because what varies between two installed
                // pages is exactly what it closes over.
                (context, controller) => {
                    context.Response.OutputFactory = OutputFactory;
                    context.Response.ResponseValue = controller.Index(configuration);
                },
                ExecutionHelper.GetFilterInfo(Metadata))) {
            _ = OutputCheck;
        }
    }
}
