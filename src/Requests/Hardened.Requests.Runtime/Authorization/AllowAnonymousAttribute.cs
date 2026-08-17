namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Makes an operation reachable without a credential, on purpose.
/// </summary>
/// <remarks>
/// <para>
/// The opt-out from <c>[RequireAuthorization]</c>. Without that, every handler is already public and
/// this says nothing; with it, a handler carrying neither a policy attribute nor this one is denied
/// at runtime and reported at build, and this is how a login endpoint or a health check says it
/// meant to be open.
/// </para>
/// <para>
/// Also what OpenAPI's <c>security: []</c> maps to. The specification distinguishes "this operation
/// inherits the document's default" from "this operation is explicitly public", and so does this:
/// an operation with no attribute has said nothing, an operation with this one has said something.
/// </para>
/// <para>
/// It carries no requirement, which is why it does not implement
/// <c>IAuthorizeAttribute</c> - it is the absence of a requirement rather than a permissive one, and
/// giving it a requirement that always passes would let it be combined with a real one and win.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowAnonymousAttribute : Attribute;
