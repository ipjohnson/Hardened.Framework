using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Reads a handler's attributes once and decides what, if anything, guards it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a global filter rather than making the attributes
/// <see cref="IRequestFilterProvider"/>s</b>, which is the more obvious route and the wrong one. An
/// attribute only ever sees itself, and the rules here are about the whole set: repeating
/// <c>[AuthorizeGrants]</c> means <em>or</em>, <c>[AllowAnonymous]</c> cancels everything, and a
/// handler carrying nothing at all still needs an answer once the application has opted in. Attribute
/// providers would also yield one filter each, so a handler with three attributes would evaluate
/// three times and refuse on the first.
/// </para>
/// <para>
/// The registry hands this the handler and its whole metadata array exactly once, which is the shape
/// the problem actually has - and the backstop for an unannotated handler falls out of the same
/// pass instead of needing a second mechanism.
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
        var metadata = handlerInfo.Metadata;

        var requirement = Fold(metadata);

        if (requirement == null) {
            // Nothing said anything about this handler. Public unless the application has said
            // otherwise, in which case being unannotated is the thing being guarded against.
            if (!_requireAuthorization || IsAnonymous(metadata)) {
                return null;
            }

            requirement = Requirement.Authenticated();
        }
        else if (IsAnonymous(metadata)) {
            // Both a requirement and an opt-out. The opt-out wins, because it is the more explicit
            // statement - somebody wrote it on this handler on purpose - and because the alternative
            // is a route that looks public in the source and refuses in production.
            return null;
        }

        // Grants alone can be settled before a body is read; anything reading bound parameters
        // cannot. Decided here, once per handler, rather than per request.
        var order = requirement.RequiresContext
            ? FilterOrder.Authorization
            : FilterOrder.Authentication + 1;

        var filter = new AuthorizationFilter(
            requirement, beforeSerialization: order < FilterOrder.Serialization);

        return new RequestFilterInfo(_ => filter, order);
    }

    private static bool IsAnonymous(IReadOnlyList<object> metadata) {
        foreach (var item in metadata) {
            if (item is AllowAnonymousAttribute) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Folds every authorization attribute on a handler into one requirement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeated <c>[AuthorizeGrants]</c> is <b>or</b>, because that attribute is what a specification
    /// becomes and the outer list of OpenAPI's <c>security</c> is a list of alternatives.
    /// </para>
    /// <para>
    /// Everything else is <b>and</b>. Two hand-written policies on one handler read as two things
    /// that must both hold - it is the stricter reading of an ambiguous one, and it matches what
    /// stacking authorization attributes means everywhere else. A generated <c>[AuthorizeGrants]</c>
    /// alongside a hand-written <c>[Authorize&lt;T&gt;]</c> therefore requires both: the
    /// specification's answer, and the extra condition somebody added on top of it.
    /// </para>
    /// </remarks>
    private static Requirement? Fold(IReadOnlyList<object> metadata) {
        List<Requirement>? alternatives = null;
        List<Requirement>? conjuncts = null;

        foreach (var item in metadata) {
            switch (item) {
                case AuthorizeGrantsAttribute grants:
                    (alternatives ??= []).Add(grants.Requirement);
                    break;

                case IAuthorizeAttribute authorize:
                    (conjuncts ??= []).Add(authorize.Requirement);
                    break;
            }
        }

        if (alternatives == null && conjuncts == null) {
            return null;
        }

        var parts = new List<Requirement>();

        if (alternatives != null) {
            parts.Add(Requirement.AnyOf(alternatives.ToArray()));
        }

        if (conjuncts != null) {
            parts.AddRange(conjuncts);
        }

        return Requirement.AllOf(parts.ToArray());
    }
}
