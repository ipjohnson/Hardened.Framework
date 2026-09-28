using System.Globalization;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hardened.Requests.Jwt;

/// <summary>
/// Validates a JWT and builds the caller it names. The validator <c>BearerPrincipalSource</c> is
/// given by <c>[JwtBearerAuthentication&lt;TScheme&gt;]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A token is accepted when its signature verifies against one of the signing keys, with an
/// asymmetric algorithm, and its <c>iss</c>, <c>aud</c>, <c>exp</c> and <c>nbf</c> are what
/// <see cref="JwtBearerConfiguration"/> allows. <c>alg: none</c> and the <c>HS</c> family are
/// refused: a symmetric key is shared with every party that can sign, so it cannot say who signed.
/// </para>
/// <para>
/// A refused token answers <see cref="AnonymousCallerPrincipal.Rejected"/> with a short reason,
/// which becomes the challenge's <c>error_description</c> when the route requires a caller.
/// </para>
/// </remarks>
public sealed class JwtBearerValidator
{
    /// <summary>The scheme an accepted caller's principal names.</summary>
    public const string SchemeName = "bearer";

    private static readonly string[] Algorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
    ];

    // Claim names stay as the token spells them. The default maps sub to a URI.
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly IJwtBearerConfiguration _configuration;
    private readonly IJwtSigningKeySource _keys;

    public JwtBearerValidator(
        IOptions<IJwtBearerConfiguration> configuration,
        IJwtSigningKeySource keys
    )
    {
        _configuration = configuration.Value;
        _keys = keys;
    }

    /// <summary>
    /// The caller the token names, or a rejected anonymous caller when the token is refused.
    /// </summary>
    public async ValueTask<ICallerPrincipal?> Validate(string token, IExecutionContext context)
    {
        if (!_handler.CanReadToken(token))
        {
            return AnonymousCallerPrincipal.Rejected("The token is not a JWT.");
        }

        JsonWebToken read;

        try
        {
            read = _handler.ReadJsonWebToken(token);
        }
        catch (ArgumentException)
        {
            return AnonymousCallerPrincipal.Rejected("The token is malformed.");
        }

        var keys = await _keys.GetKeys(
            string.IsNullOrEmpty(read.Kid) ? null : read.Kid,
            context.CancellationToken
        );

        var result = await _handler.ValidateTokenAsync(read, Parameters(keys));

        return result.IsValid && result.SecurityToken is JsonWebToken accepted
            ? Principal(accepted)
            : AnonymousCallerPrincipal.Rejected(Describe(result.Exception));
    }

    private TokenValidationParameters Parameters(IReadOnlyList<SecurityKey> keys) =>
        new()
        {
            ValidIssuer = _configuration.Issuer,
            ValidAudience = _configuration.Audience,
            IssuerSigningKeys = keys,
            ValidAlgorithms = Algorithms,
            ClockSkew = TimeSpan.FromSeconds(_configuration.ClockSkewSeconds),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
        };

    private CallerPrincipal Principal(JsonWebToken token)
    {
        var grants = new List<string>();
        var claims = new List<KeyValuePair<string, string>>();

        foreach (var claim in token.Claims)
        {
            claims.Add(new KeyValuePair<string, string>(claim.Type, claim.Value));

            if (string.Equals(claim.Type, _configuration.GrantClaim, StringComparison.Ordinal))
            {
                grants.AddRange(claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return new CallerPrincipal(
            SchemeName,
            grants,
            string.IsNullOrEmpty(token.Subject) ? null : token.Subject,
            token.Issuer,
            claims
        );
    }

    /// <remarks>
    /// Short and fixed, because it goes back to the caller. The more specific exceptions derive from
    /// the general ones, so they are matched first.
    /// </remarks>
    internal static string Describe(Exception? failure) =>
        failure switch
        {
            SecurityTokenExpiredException expired => "The token expired at "
                + expired.Expires.ToString("o", CultureInfo.InvariantCulture)
                + ".",
            SecurityTokenNotYetValidException early => "The token is not valid until "
                + early.NotBefore.ToString("o", CultureInfo.InvariantCulture)
                + ".",
            SecurityTokenNoExpirationException => "The token has no expiry.",
            SecurityTokenInvalidAudienceException => "The token is not for this audience.",
            SecurityTokenInvalidIssuerException => "The token's issuer is not accepted.",
            SecurityTokenInvalidAlgorithmException => "The token's algorithm is not accepted.",
            SecurityTokenSignatureKeyNotFoundException => "No known key signed the token.",
            SecurityTokenInvalidSignatureException => "The token's signature is invalid.",
            SecurityTokenMalformedException => "The token is malformed.",
            _ => "The token is invalid.",
        };
}
