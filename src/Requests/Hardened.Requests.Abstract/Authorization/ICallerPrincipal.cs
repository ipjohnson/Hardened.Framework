using System.Diagnostics.CodeAnalysis;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// The caller a request is running as.
/// </summary>
/// <remarks>
/// <para>
/// The value never mutates; the slot holding it on <see cref="Execution.IExecutionContext"/> may be
/// replaced. Because the value is immutable, replacing it costs a reference assignment - there is no
/// deep copy on <c>Clone</c> or <c>Fork</c>, so the swappable slot carries no copying cost.
/// </para>
/// <para>
/// <b>Deliberately not <c>System.Security.Claims.ClaimsPrincipal</c>.</b> That type mutates in place
/// through <c>AddIdentity</c>, which is precisely the operation this design refuses to have -
/// adopting it would mean documenting "do not use half of this type's API". It is also
/// reflection-heavy, against the grain of a framework that source-generates its serialization. An
/// adapter for applications that already have one belongs in the AspNetCore runtime.
/// </para>
/// <para>
/// <see cref="IsAuthenticated"/> is derived from <see cref="AuthenticationScheme"/> rather than
/// stored, which makes the invalid states unrepresentable: there is no principal that claims to be
/// authenticated with no scheme, and none that carries a scheme without being authenticated.
/// </para>
/// </remarks>
public interface ICallerPrincipal {
    /// <summary>
    /// The scheme that authenticated this caller, or null when anonymous.
    /// </summary>
    string? AuthenticationScheme { get; }

    /// <summary>
    /// True once a credential has been presented and accepted.
    /// </summary>
    bool IsAuthenticated => AuthenticationScheme is not null;

    /// <summary>
    /// The grants this caller holds. Empty rather than null when anonymous, so a policy walks the
    /// same code path either way.
    /// </summary>
    IReadOnlySet<string> Grants { get; }

    /// <summary>
    /// Who the caller is, as the credential named them - an OAuth <c>sub</c>. Null when anonymous.
    /// </summary>
    string? Subject { get; }

    /// <summary>
    /// Who vouched for the caller. Null when anonymous.
    /// </summary>
    /// <remarks>
    /// This is the issuer that was <em>accepted</em>, having already been matched against the
    /// configured allowlist. It is never the raw value read out of an unverified token.
    /// </remarks>
    string? Issuer { get; }

    /// <summary>
    /// Reads a claim the credential carried.
    /// </summary>
    bool TryGetClaim(string name, [MaybeNullWhen(false)] out string value);
}
