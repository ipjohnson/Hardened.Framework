namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// A way a caller is established, named as a type.
/// </summary>
/// <remarks>
/// <para>
/// A marker, deliberately. The scheme's document shape rides an attribute on the implementing
/// type - <see cref="HttpAuthenticationSchemeAttribute"/> and friends - because the source
/// generator reads attributes and cannot execute members. The type itself is the identity:
/// <c>[Authorize&lt;BearerAuth&gt;]</c> names it, "find references" finds every operation that
/// requires it, and the published document's <c>securitySchemes</c> entry is keyed by the type's
/// name. Declaring a scheme is therefore just declaring the type; using it anywhere puts it in
/// the document.
/// </para>
/// <para>
/// The scheme says how a caller is established, and nothing about what they may do. Grants stay
/// scheme-less on purpose - <c>IActivityAuthorizationService</c>'s contributors may resolve them
/// from the credential or from a store - so the same grant can be satisfiable under two schemes,
/// and two callers on one scheme can hold different grants.
/// </para>
/// </remarks>
public interface IAuthenticationScheme;
