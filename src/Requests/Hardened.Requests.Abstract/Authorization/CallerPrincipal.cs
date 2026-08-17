using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// An authenticated caller, built once by whatever validated the credential.
/// </summary>
/// <remarks>
/// <para>
/// The constructor takes a non-null <paramref name="authenticationScheme"/> because an instance of
/// this type is authenticated by construction - <see cref="AnonymousCallerPrincipal"/> is the other
/// case, and there is no way to build a half-authenticated one of either.
/// </para>
/// <para>
/// Grants and claims are frozen on the way in. They are read many times per request and written
/// once, and freezing them is also what makes the "immutable value" contract in
/// <see cref="ICallerPrincipal"/> true rather than merely intended - a caller handed an
/// <c>IReadOnlySet</c> backed by a live <c>HashSet</c> could still cast it back and edit it.
/// </para>
/// </remarks>
public sealed class CallerPrincipal : ICallerPrincipal {
    private readonly FrozenDictionary<string, string> _claims;

    public CallerPrincipal(
        string authenticationScheme,
        IEnumerable<string>? grants = null,
        string? subject = null,
        string? issuer = null,
        IEnumerable<KeyValuePair<string, string>>? claims = null) {
        if (string.IsNullOrEmpty(authenticationScheme)) {
            throw new ArgumentException(
                "An authenticated principal must name the scheme that authenticated it. Use " +
                nameof(AnonymousCallerPrincipal) + "." + nameof(AnonymousCallerPrincipal.Instance) +
                " for a caller that presented no credential.",
                nameof(authenticationScheme));
        }

        AuthenticationScheme = authenticationScheme;
        Subject = subject;
        Issuer = issuer;

        Grants = grants is null
            ? FrozenSet<string>.Empty
            : grants.ToFrozenSet(StringComparer.Ordinal);

        // Ordinal, because a claim name is a wire identifier rather than prose. Last one wins on a
        // duplicate, which ToFrozenDictionary would otherwise throw on.
        _claims = claims is null
            ? FrozenDictionary<string, string>.Empty
            : claims
                .GroupBy(c => c.Key, StringComparer.Ordinal)
                .ToFrozenDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
    }

    public string? AuthenticationScheme { get; }

    /// <summary>
    /// Ordinal comparison. A grant is matched against a value the spec declared, and <c>pets:read</c>
    /// and <c>Pets:Read</c> are different scopes to every authorization server.
    /// </summary>
    public IReadOnlySet<string> Grants { get; }

    public string? Subject { get; }

    public string? Issuer { get; }

    public bool TryGetClaim(string name, [MaybeNullWhen(false)] out string value) =>
        _claims.TryGetValue(name, out value);
}
