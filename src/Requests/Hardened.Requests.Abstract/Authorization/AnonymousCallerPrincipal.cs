using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// The principal every request starts with: no scheme, no grants, no claims.
/// </summary>
/// <remarks>
/// <para>
/// A well-known instance rather than null. No call site needs a null check, and "no credential was
/// presented" becomes a real value rather than an absence - which is what lets a policy evaluate
/// against an anonymous caller instead of having to special-case one.
/// </para>
/// <para>
/// Immutable and stateless, so a single instance serves every request on every thread.
/// </para>
/// </remarks>
public sealed class AnonymousCallerPrincipal : ICallerPrincipal {
    public static readonly ICallerPrincipal Instance = new AnonymousCallerPrincipal();

    private AnonymousCallerPrincipal() { }

    /// <summary>Null, which is what makes <see cref="ICallerPrincipal.IsAuthenticated"/> false.</summary>
    public string? AuthenticationScheme => null;

    public IReadOnlySet<string> Grants => FrozenSet<string>.Empty;

    public string? Subject => null;

    public string? Issuer => null;

    public bool TryGetClaim(string name, [MaybeNullWhen(false)] out string value) {
        value = null;
        return false;
    }
}
