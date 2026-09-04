using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Reads a handler's requirements once and decides what, if anything, guards it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a global filter rather than making the attributes
/// <see cref="IRequestFilterProvider"/>s</b>, which is the more obvious route and the wrong one. An
/// attribute only ever sees itself, and two of the rules here are about the whole handler:
/// <c>[AllowAnonymous]</c> cancels everything, and a handler carrying nothing at all still needs an
/// answer once the application has opted in. Attribute providers would also yield one filter each,
/// so a handler with three attributes would evaluate three times and refuse on the first.
/// </para>
/// <para>
/// The registry hands this the handler exactly once, which is the shape the problem actually has -
/// and the backstop for an unannotated handler falls out of the same pass instead of needing a
/// second mechanism.
/// </para>
/// <para>
/// It reads <see cref="IExecutionRequestHandlerInfo.Requirements"/> rather than walking metadata
/// itself, so a requirement a convention added while the handler was built is honoured exactly like
/// one an attribute declared.
/// </para>
/// </remarks>
public class AuthorizationFilterProvider {
    private readonly bool _requireAuthorization;

    /// <param name="requireAuthorization">
    /// Whether a handler carrying no authorization attribute is denied rather than public.
    /// </param>
    public AuthorizationFilterProvider(bool requireAuthorization) {
        _requireAuthorization = requireAuthorization;
    }

    public RequestFilterInfo? GetFilter(IExecutionRequestHandlerInfo handlerInfo) {
        var requirement = handlerInfo.Requirement;

        if (requirement == null) {
            // Nothing said anything about this handler. Public unless the application has said
            // otherwise, in which case being unannotated is the thing being guarded against.
            if (!_requireAuthorization || IsAnonymous(handlerInfo.Metadata)) {
                return null;
            }

            requirement = Requirement.Authenticated();
        }
        else if (IsAnonymous(handlerInfo.Metadata)) {
            // Both a requirement and an opt-out. The opt-out wins, because it is the more explicit
            // statement - somebody wrote it on this handler on purpose - and because the alternative
            // is a route that looks public in the source and refuses in production.
            return null;
        }

        // Grants alone can be settled before a body is read; anything reading bound parameters
        // cannot. Decided here, once per handler, rather than per request.
        var order = requirement.RequiresContext
            ? FilterOrder.Authorization
            : FilterOrder.GrantAuthorization;

        var filter = new AuthorizationFilter(
            requirement, beforeSerialization: order < FilterOrder.Serialization);

        return new RequestFilterInfo(_ => filter, order, nameof(AuthorizationFilter));
    }

    private static bool IsAnonymous(IReadOnlyList<object> metadata) {
        foreach (var item in metadata) {
            if (item is AllowAnonymousAttribute) {
                return true;
            }
        }

        return false;
    }
}
