using Microsoft.IdentityModel.Tokens;

namespace Hardened.Requests.Jwt;

/// <summary>
/// The keys a token's signature is checked against.
/// </summary>
/// <remarks>
/// <see cref="JwksSigningKeySource"/> reads them from the issuer's JWKS.
/// <c>Hardened.Requests.Jwt.Testing</c> replaces it with a test issuer's key.
/// </remarks>
public interface IJwtSigningKeySource
{
    /// <summary>
    /// The keys to check a token against.
    /// </summary>
    /// <param name="keyId">
    /// The token's <c>kid</c>, or null when it names none. A source may fetch its keys again when
    /// it does not know this one.
    /// </param>
    /// <param name="cancellationToken">The request's token.</param>
    ValueTask<IReadOnlyList<SecurityKey>> GetKeys(
        string? keyId,
        CancellationToken cancellationToken
    );
}
