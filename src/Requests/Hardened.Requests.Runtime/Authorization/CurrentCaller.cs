using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// The scoped holder <see cref="ICurrentCaller"/> resolves to.
/// </summary>
/// <remarks>
/// <para>
/// Written by <see cref="AuthenticationMiddleware"/>, which runs ahead of the whole handler chain
/// inside the request's own scope, and read by whatever the handler was resolved with. A mutable
/// holder rather than a factory over the execution context, because the container has no per-request
/// instance of the context to hand one.
/// </para>
/// <para>
/// <b>What it reflects, exactly.</b> The principal the authentication seam established. An
/// application with no <see cref="IPrincipalSource"/> installs no middleware and every request
/// reads <see cref="AnonymousCallerPrincipal.Instance"/> - which is the same answer
/// <c>IExecutionContext.CallerPrincipal</c> gives, at no per-request cost. A principal assigned to
/// the context by something else after the middleware has run is not reflected here; nothing else
/// is asked to run, which is what keeps this free for the applications that use none of it.
/// </para>
/// </remarks>
internal sealed class CurrentCaller : ICurrentCaller {
    public ICallerPrincipal Principal { get; set; } = AnonymousCallerPrincipal.Instance;
}
