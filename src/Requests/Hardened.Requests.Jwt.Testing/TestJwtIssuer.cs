using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hardened.Requests.Jwt.Testing;

/// <summary>
/// Signs tokens for a test, with an RSA key generated for the test run. The application under test
/// trusts that key in place of its JWKS, so a test drives the real validator.
/// </summary>
/// <remarks>
/// <see cref="JwtTestIssuerAttribute"/> registers it. A test takes it as a parameter:
/// <code>
/// [ModuleTest]
/// public async Task AddsATodo(ITestWebApp app, TestJwtIssuer issuer) {
///     var token = issuer.Token(jwt => jwt.Grants = ["todos:write"]);
///     ...
/// }
/// </code>
/// </remarks>
public sealed class TestJwtIssuer : IJwtSigningKeySource
{
    /// <summary>The <c>iss</c> tokens carry when the application configures none.</summary>
    public const string DefaultIssuer = "https://issuer.hardened.test";

    /// <summary>The <c>aud</c> tokens carry when the application configures none.</summary>
    public const string DefaultAudience = "hardened-test";

    private static readonly Lazy<RsaSecurityKey> Trusted = new(() => Key("hardened-test"));

    private static readonly Lazy<RsaSecurityKey> Untrusted = new(() => Key("hardened-untrusted"));

    private readonly IJwtBearerConfiguration _configuration;
    private readonly JsonWebTokenHandler _handler = new()
    {
        SetDefaultTimesOnTokenCreation = false,
    };

    public TestJwtIssuer(IOptions<IJwtBearerConfiguration> configuration)
    {
        _configuration = configuration.Value;
    }

    /// <summary>
    /// A signed token. With no <paramref name="shape"/>, one the application accepts.
    /// </summary>
    public string Token(Action<TestJwt>? shape = null)
    {
        var jwt = new TestJwt
        {
            Issuer = _configuration.Issuer,
            Audience = _configuration.Audience,
        };

        shape?.Invoke(jwt);

        var now = DateTime.UtcNow;
        var expires = jwt.Expires ?? now.AddHours(1);
        var notBefore = jwt.NotBefore ?? (expires < now ? expires.AddHours(-1) : now);

        var claims = new Dictionary<string, object>(jwt.Claims);

        if (jwt.Subject != null)
        {
            claims["sub"] = jwt.Subject;
        }

        if (jwt.Grants.Count > 0)
        {
            claims[_configuration.GrantClaim] = string.Join(' ', jwt.Grants);
        }

        return _handler.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = jwt.Issuer,
                Audience = jwt.Audience,
                Claims = claims,
                IssuedAt = notBefore,
                NotBefore = notBefore,
                Expires = expires,
                SigningCredentials = new SigningCredentials(
                    jwt.SignedByUntrustedKey ? Untrusted.Value : Trusted.Value,
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );
    }

    /// <summary>The test run's key, whatever the token names.</summary>
    public ValueTask<IReadOnlyList<SecurityKey>> GetKeys(
        string? keyId,
        CancellationToken cancellationToken
    ) => new([Trusted.Value]);

    private static RsaSecurityKey Key(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };
}
