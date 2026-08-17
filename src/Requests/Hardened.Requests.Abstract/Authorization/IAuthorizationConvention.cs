using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// Adds a requirement to handlers that did not ask for one by name.
/// </summary>
/// <remarks>
/// <para>
/// The runtime half of declaring authorization. An attribute states what one handler needs at the
/// place it is written; this states what a whole class of handlers needs, decided from what the
/// handler already is - "everything under <c>/admin</c> requires <c>admin:access</c>", "every
/// non-GET on a tenant route requires <c>tenant:write</c>" - without that rule being copied onto
/// every method it covers, where it would drift the first time somebody adds a route.
/// </para>
/// <example>
/// <code>
/// public class AdminRoutesAreAdminOnly : IAuthorizationConvention {
///     public Requirement? Apply(IExecutionRequestHandlerInfo handler) =>
///         handler.Path.StartsWith("/admin") ? Requirement.Grant("admin:access") : null;
/// }
/// </code>
/// </example>
/// <para>
/// <b>Applied while the handler is being constructed, before its info is handed to anything.</b>
/// What a convention returns is conjoined with what the handler declared, and the combined
/// requirement is what lands on <see cref="IExecutionRequestHandlerInfo.Requirement"/> - so the
/// authorization filter, the execution context, and anything else reading a handler all see one
/// answer. There is no second, effective view for the two to disagree about.
/// </para>
/// <para>
/// <b>It can only narrow.</b> The result is conjoined, never substituted, so a convention cannot
/// weaken a handler that declared its own requirement and cannot be defeated by one - which is what
/// makes it safe for a convention to be the thing standing between an unannotated route and the
/// world. <c>[AllowAnonymous]</c> remains the single exception, for the reason it always was: a
/// route that reads as public in the source must not refuse in production.
/// </para>
/// <para>
/// Asked once per handler, as its filter chain is built. Returning null is the normal answer for
/// most handlers and costs nothing.
/// </para>
/// </remarks>
public interface IAuthorizationConvention {
    /// <summary>
    /// What this convention requires of <paramref name="handlerInfo"/>, or null if it has nothing
    /// to say about it.
    /// </summary>
    /// <remarks>
    /// The handler is passed in fully formed - path, method, handler type, parameters and metadata -
    /// so a convention can key off any of them. It is read-only: a convention inspects and returns,
    /// it does not amend.
    /// </remarks>
    Requirement? Apply(IExecutionRequestHandlerInfo handlerInfo);
}
