using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Runtime.Hosting;

/// <summary>
/// One function invocation, from what the worker bound to what the host is told.
/// </summary>
/// <remarks>
/// <para>
/// The third of the three decisions a host makes. The first two - how trigger data becomes a
/// request and how a response becomes what the host expects - are <see cref="ITriggerAdapter"/>.
/// This is the loop that owns the invocation: it picks the adapter, opens a scope, hands the
/// context to <see cref="IRequestExecutor"/> and lets the adapter write the answer. Everything
/// between is the shared pipeline, which no host reimplements.
/// </para>
/// <para>
/// The generated shims are what call it. Each is the <c>[Function]</c> the host invokes for one
/// trigger, and its body is one line: <see cref="Invoke(FunctionContext, string, string, object, FunctionsDispatch)"/>
/// with the route it was generated for and whatever the worker bound. The counterpart of
/// <c>LambdaInvocationHandler</c>, and the differences are the genuine ones: no payload to buffer
/// and nothing to peek at, because the host already knows which function it is invoking.
/// </para>
/// </remarks>
public class FunctionsInvocationHandler {
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IRequestExecutor _executor;
    private readonly IMetricLoggerProvider _metricLoggerProvider;
    private readonly ITriggerAdapter[] _adapters;
    private readonly object _install = new();

    public FunctionsInvocationHandler(
        IServiceProvider rootServiceProvider,
        IRequestExecutor executor,
        IMetricLoggerProvider metricLoggerProvider,
        IEnumerable<ITriggerAdapter> adapters) {
        _rootServiceProvider = rootServiceProvider;
        _executor = executor;
        _metricLoggerProvider = metricLoggerProvider;
        _adapters = adapters.ToArray();
    }

    /// <summary>The adapters this worker was built with, in registration order.</summary>
    public IReadOnlyList<ITriggerAdapter> Adapters => _adapters;

    /// <summary>
    /// What a generated shim calls: the handler off the invocation's services, invoked with the
    /// route the shim was generated for.
    /// </summary>
    /// <remarks>
    /// Resolved from <see cref="FunctionContext.InstanceServices"/> rather than injected, because a
    /// shim is a static method the worker locates by name and the host constructs nothing for it.
    /// <c>UseHardened</c> is what registered the handler there.
    /// </remarks>
    public static Task Invoke(
        FunctionContext context, string scheme, string path, object data,
        FunctionsDispatch dispatch = FunctionsDispatch.Trigger) =>
        context.InstanceServices.GetRequiredService<FunctionsInvocationHandler>()
            .Invoke(new FunctionsTrigger(scheme, path, data, dispatch), context);

    /// <summary>
    /// Runs one invocation. A throw reaches the worker, which reports the invocation failed to the
    /// host - and for a queue that is what makes the messages redeliver.
    /// </summary>
    public async Task Invoke(FunctionsTrigger trigger, FunctionContext functionContext) {
        var adapter = Select(trigger);

        Install();

        using var scope = _rootServiceProvider.CreateScope();

        var body = new MemoryStream();

        var context = new FunctionsExecutionContext(
            _rootServiceProvider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            adapter.CreateRequest(trigger, functionContext),
            adapter.CreateResponse(body),
            functionContext,
            _metricLoggerProvider.CreateLogger("functions-invocation")) {
            Dispatch = trigger.Dispatch
        };

        await _executor.Run(context, adapter.FailurePolicy);

        await adapter.WriteResponse(context, functionContext);
    }

    private bool _installed;

    /// <summary>
    /// Puts dispatch at the end of the middleware chain, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same thing <c>KestrelServerRunner</c> does at start and <c>LambdaInvocationHandler</c>
    /// does on its first invocation, and for the same reason it lives outside a constructor:
    /// <c>MiddlewareService</c> is a singleton holding a plain list, so appending twice puts two
    /// copies of dispatch in every chain. Under a lock, because the worker runs invocations
    /// concurrently and two first invocations would otherwise both append.
    /// </para>
    /// <para>
    /// <b>Not the Lambda rule.</b> <c>LambdaInvocationHandler</c> refuses an application that
    /// registers both kinds of <c>IHandlerDispatch</c>, because on Lambda the web routes and the
    /// triggers are separate functions with separate failure policies. On Azure one worker hosts
    /// every function of the application, and a Hardened application with <c>[Get]</c> routes
    /// beside a <c>[Queue]</c> handler is ordinary. So what is installed is a selector: each
    /// generated shim says whether it is the HTTP catch-all or a trigger, and the selector runs
    /// whichever dispatch that invocation asked for - <c>FunctionDispatchFilter</c> for a trigger,
    /// the web routing table for HTTP.
    /// </para>
    /// </remarks>
    private void Install() {
        if (_installed) {
            return;
        }

        lock (_install) {
            if (_installed) {
                return;
            }

            var dispatch = _rootServiceProvider.GetServices<IHandlerDispatch>().ToArray();

            if (dispatch.Length == 0) {
                throw new InvalidOperationException(
                    "This application declares no handlers. A verb attribute or a trigger attribute " +
                    "on a method is what compiles one.");
            }

            _rootServiceProvider.GetRequiredService<IMiddlewareService>()
                .Use(_ => new DispatchSelector(dispatch));

            _installed = true;
        }
    }

    /// <summary>
    /// The last filter in every chain: the dispatch the invocation asked for.
    /// </summary>
    private sealed class DispatchSelector : IHandlerDispatch {
        private readonly IHandlerDispatch? _triggers;
        private readonly IHandlerDispatch? _web;

        public DispatchSelector(IReadOnlyList<IHandlerDispatch> registered) {
            foreach (var dispatch in registered) {
                // FunctionDispatchFilter is the one dispatch this package can name: it lives in
                // Hardened.Requests.Runtime beside the executor. The web routing table is whatever
                // else was registered, because naming it would reference the web package.
                if (dispatch is FunctionDispatchFilter) {
                    _triggers ??= dispatch;
                }
                else {
                    _web ??= dispatch;
                }
            }
        }

        public Task Execute(IExecutionChain chain) {
            var context = chain.Context as FunctionsExecutionContext;

            var wanted = context?.Dispatch ?? FunctionsDispatch.Trigger;

            var dispatch = wanted == FunctionsDispatch.Web ? _web : _triggers;

            if (dispatch == null) {
                throw new InvalidOperationException(
                    wanted == FunctionsDispatch.Web
                        ? "This invocation is the HTTP function and the application registered no " +
                          "web routing table. A verb attribute on a method is what compiles one."
                        : "This invocation is a trigger and the application registered no function " +
                          "handler table. A trigger attribute on a method is what compiles one.");
            }

            return dispatch.Execute(chain);
        }
    }

    /// <summary>
    /// Which adapter binds this invocation's data.
    /// </summary>
    /// <remarks>
    /// Matching nothing is an error, not a fallback. A shim is generated for a family whose
    /// runtime package is referenced, so data no adapter recognises means the generator and the
    /// registered modules disagree - a module removed from the application after the shim was
    /// written, most likely.
    /// </remarks>
    private ITriggerAdapter Select(FunctionsTrigger trigger) {
        foreach (var adapter in _adapters) {
            if (adapter.Handles(trigger)) {
                return adapter;
            }
        }

        throw new InvalidOperationException(
            $"No trigger adapter binds {trigger.Data.GetType().Name}, which the function for " +
            $"{trigger.Scheme} {trigger.Path} received. The worker is built with " +
            (_adapters.Length == 0
                ? "no adapter at all"
                : string.Join(", ", _adapters.Select(adapter => adapter.GetType().Name))) +
            ", so either the adapter package for this trigger is not referenced or its module was " +
            "removed from the application.");
    }
}
