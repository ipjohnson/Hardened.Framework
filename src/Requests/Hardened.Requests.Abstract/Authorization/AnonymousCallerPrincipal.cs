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
/// Immutable, so <see cref="Instance"/> serves every request on every thread. A caller whose
/// credential was refused is anonymous too, and <see cref="Rejected"/> builds one that says so.
/// </para>
/// </remarks>
public sealed class AnonymousCallerPrincipal : ICallerPrincipal
{
    public static readonly ICallerPrincipal Instance = new AnonymousCallerPrincipal(false, null);

    private AnonymousCallerPrincipal(bool credentialRejected, string? rejectionDescription)
    {
        CredentialRejected = credentialRejected;
        RejectionDescription = rejectionDescription;
    }

    /// <summary>
    /// An anonymous caller that presented a credential its source refused, such as an expired
    /// token.
    /// </summary>
    /// <remarks>
    /// A principal source returns this rather than null for a credential that is its own and is not
    /// valid. The request continues anonymously, as it does after null. A requirement then refuses
    /// it with 401 and <c>error="invalid_token"</c> rather than the plain challenge a caller with no
    /// credential gets, so the client knows to replace its token rather than obtain one.
    /// </remarks>
    /// <param name="description">
    /// Why the credential was refused, sent as the challenge's <c>error_description</c>. Null sends
    /// none.
    /// </param>
    public static ICallerPrincipal Rejected(string? description = null) =>
        new AnonymousCallerPrincipal(true, description);

    /// <summary>
    /// True when the request presented a credential and its source refused it.
    /// </summary>
    public bool CredentialRejected { get; }

    /// <summary>
    /// Why the credential was refused, or null when the source did not say or nothing was refused.
    /// </summary>
    public string? RejectionDescription { get; }

    /// <summary>Null, which is what makes <see cref="ICallerPrincipal.IsAuthenticated"/> false.</summary>
    public string? AuthenticationScheme => null;

    public IReadOnlySet<string> Grants => FrozenSet<string>.Empty;

    public string? Subject => null;

    public string? Issuer => null;

    public bool TryGetClaim(string name, [MaybeNullWhen(false)] out string value)
    {
        value = null;
        return false;
    }
}
