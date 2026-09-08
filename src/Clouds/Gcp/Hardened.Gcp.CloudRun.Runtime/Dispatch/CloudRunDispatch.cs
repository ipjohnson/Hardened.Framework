using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Gcp.CloudRun.Runtime.Dispatch;

/// <summary>
/// One dispatch for a host that serves everything over HTTP: trigger schemes to the function
/// table, HTTP verbs to the routing table.
/// </summary>
/// <remarks>
/// <para>
/// A Lambda function serves one family and refuses two dispatches, because a web route answers a
/// caller on a connection and a trigger fails the invocation to make its source redeliver. On
/// Cloud Run both arrive on the same socket and both answer with a status - a Pub/Sub push reads
/// anything outside 102, 200, 201, 202 and 204 as a negative acknowledgement - so one service can
/// serve both, and the only question is which table a request routes through. The scheme answers
/// it: <c>QUEUE /orders</c> is a trigger the front door built, <c>GET /orders</c> is a web route.
/// </para>
/// <para>
/// <b>This is what makes an application on <c>[CloudRunRuntime]</c> hold exactly one
/// <see cref="IHandlerDispatch"/>.</b> <c>[KestrelRuntime]</c> brings the routing table as one and
/// the function generator registers <see cref="FunctionDispatchFilter"/> as another; the neutral
/// test delivery and the Lambda loop both refuse a container holding two. <see cref="Compose"/>
/// replaces the pair with an instance of this, registered under
/// <see cref="IWebExecutionHandlerService"/> as well, because <c>KestrelServerRunner</c> resolves
/// the routing table by that interface and appends it without asking for
/// <see cref="IHandlerDispatch"/> at all.
/// </para>
/// <para>
/// A trigger scheme that arrives over the socket rather than from the front door reaches the
/// function table too. That adds nothing an envelope could not already do - a caller who can send
/// <c>QUEUE /orders</c> can send a push body - and it is why a push endpoint is protected by the
/// deployment, with Cloud Run's own authentication and a push subscription that carries a token,
/// rather than by the shape of the request.
/// </para>
/// </remarks>
public sealed class CloudRunDispatch : IWebExecutionHandlerService {
    /// <summary>
    /// The schemes the function table serves: every neutral trigger the framework declares.
    /// </summary>
    public static readonly IReadOnlySet<string> TriggerSchemes = new HashSet<string>(StringComparer.Ordinal) {
        "QUEUE", "TOPIC", "TIMER", "EVENT", "CHANGE", "STREAM", "BLOB", "INVOKE"
    };

    public CloudRunDispatch(IHandlerDispatch web, IHandlerDispatch? function) {
        Web = web;
        Function = function;
    }

    /// <summary>The routing table, for everything that is not a trigger.</summary>
    public IHandlerDispatch Web { get; }

    /// <summary>
    /// The function table, or null when the application compiled no trigger handlers - in which
    /// case a trigger scheme routes through the web table and is a 404 there.
    /// </summary>
    public IHandlerDispatch? Function { get; }

    public Task Execute(IExecutionChain chain) {
        if (Function != null && TriggerSchemes.Contains(chain.Context.Request.Method)) {
            return Function.Execute(chain);
        }

        return Web.Execute(chain);
    }

    /// <summary>
    /// Replaces a routing table and a function dispatch in <paramref name="services"/> with one
    /// of these, when both are registered. A collection holding one dispatch, or something other
    /// than that pair, is left as it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routing table is kept as whatever was registered under
    /// <see cref="IWebExecutionHandlerService"/>, re-registered under its own type, so an
    /// application that substituted the framework's keeps its own. The function dispatch is
    /// recognised by type, because that is how the generator registers it.
    /// </para>
    /// <para>
    /// Idempotent: a collection this has already composed holds one dispatch and is left alone,
    /// so an application that reaches the decorator twice is not composed twice.
    /// </para>
    /// </remarks>
    public static void Compose(IServiceCollection services) {
        ServiceDescriptor? function = null;
        var others = 0;

        foreach (var descriptor in services) {
            if (descriptor.ServiceType != typeof(IHandlerDispatch)) {
                continue;
            }

            if (descriptor.ImplementationType == typeof(FunctionDispatchFilter)) {
                function = descriptor;
            }
            else {
                others++;
            }
        }

        // One family, or something this was not written for: the existing checks in the hosts
        // and the test delivery name a container holding two of one kind better than a silent
        // composition would.
        if (function == null || others != 1) {
            return;
        }

        var web = FindWeb(services);

        if (web == null) {
            return;
        }

        services.RemoveAll<IHandlerDispatch>();
        services.RemoveAll<IWebExecutionHandlerService>();

        var resolveWeb = Rebase(services, web);

        services.TryAddSingleton<FunctionDispatchFilter>();

        services.AddSingleton<IWebExecutionHandlerService>(provider =>
            new CloudRunDispatch(resolveWeb(provider), provider.GetRequiredService<FunctionDispatchFilter>()));

        services.AddSingleton<IHandlerDispatch>(provider =>
            provider.GetRequiredService<IWebExecutionHandlerService>());
    }

    private static ServiceDescriptor? FindWeb(IServiceCollection services) {
        foreach (var descriptor in services) {
            if (descriptor.ServiceType == typeof(IWebExecutionHandlerService)) {
                return descriptor;
            }
        }

        return null;
    }

    /// <summary>
    /// How to resolve what <paramref name="web"/> registered, once it is no longer registered
    /// under the interface the composite takes over.
    /// </summary>
    private static Func<IServiceProvider, IHandlerDispatch> Rebase(IServiceCollection services, ServiceDescriptor web) {
        if (web.ImplementationInstance is IHandlerDispatch instance) {
            return _ => instance;
        }

        if (web.ImplementationFactory is { } factory) {
            return provider => (IHandlerDispatch)factory(provider);
        }

        var implementation = web.ImplementationType!;

        // The concrete type as itself, so the container builds it with its own dependencies
        // exactly as it would have under the interface.
        services.TryAdd(new ServiceDescriptor(implementation, implementation, web.Lifetime));

        return provider => (IHandlerDispatch)provider.GetRequiredService(implementation);
    }
}
