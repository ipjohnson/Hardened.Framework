using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Timeouts;

/// <summary>
/// Bounds handlers that did not state a budget by name.
/// </summary>
/// <remarks>
/// <para>
/// The runtime half of declaring a deadline, and the mirror of <c>IAuthorizationConvention</c>. An
/// attribute states what one operation may take at the place it is written; this states what a
/// whole class of them may take, decided from what the handler already is - "nothing under
/// <c>/search</c> may take more than two seconds" - without that rule being copied onto every
/// method it covers, where it would drift the first time somebody adds a route.
/// </para>
/// <example>
/// <code>
/// public class SearchIsAlwaysFast : IRequestTimeoutConvention {
///     public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handler) =>
///         handler.Path.StartsWith("/search") ? new TimeoutPolicy(2000) : null;
/// }
/// </code>
/// </example>
/// <para>
/// <b>It can only tighten.</b> What a convention returns is folded in with
/// <see cref="TimeoutPolicy.Tighter"/>, never substituted, so a convention can bound a handler that
/// declared nothing and can shorten one that declared too much - but cannot hand an operation that
/// wrote <c>[Timeout(Milliseconds = 2000)]</c> a minute. Loosening is the one direction where a
/// rule written far from the handler is more likely to be wrong than the handler is.
/// </para>
/// <para>
/// Applied after the cascade has resolved, so what it sees in
/// <see cref="IExecutionRequestHandlerInfo.Timeout"/> is whatever the operation, its class, its
/// assembly or the entry point already said.
/// </para>
/// <para>
/// Asked once per handler, as its filter chain is built. Returning null is the normal answer for
/// most handlers and costs nothing.
/// </para>
/// </remarks>
public interface IRequestTimeoutConvention {

    /// <summary>
    /// The budget this convention would put on <paramref name="handlerInfo"/>, or null if it has
    /// nothing to say about it.
    /// </summary>
    TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handlerInfo);
}
