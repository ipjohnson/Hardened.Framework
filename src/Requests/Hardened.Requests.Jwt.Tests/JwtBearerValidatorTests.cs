using System.Security.Cryptography;
using System.Text;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Jwt.Testing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Jwt.Tests;

/// <summary>
/// Which tokens the validator accepts, what caller an accepted one becomes, and what a refused one
/// is told.
/// </summary>
public class JwtBearerValidatorTests
{
    private static IOptions<IJwtBearerConfiguration> Configuration(string grantClaim = "scope") =>
        Options.Create<IJwtBearerConfiguration>(
            new JwtBearerConfiguration
            {
                Issuer = TestJwtIssuer.DefaultIssuer,
                Audience = TestJwtIssuer.DefaultAudience,
                GrantClaim = grantClaim,
                ClockSkewSeconds = 60,
            }
        );

    private static (JwtBearerValidator Validator, TestJwtIssuer Issuer) Build(
        string grantClaim = "scope"
    )
    {
        var configuration = Configuration(grantClaim);
        var issuer = new TestJwtIssuer(configuration);

        return (new JwtBearerValidator(configuration, issuer), issuer);
    }

    private static IExecutionContext Context() => Substitute.For<IExecutionContext>();

    private static async Task<AnonymousCallerPrincipal> Rejected(
        JwtBearerValidator validator,
        string token
    )
    {
        var answer = await validator.Validate(token, Context());

        var rejected = Assert.IsType<AnonymousCallerPrincipal>(answer);

        Assert.True(rejected.CredentialRejected);

        return rejected;
    }

    [Fact]
    public async Task AnAcceptedTokenIsItsCaller()
    {
        var (validator, issuer) = Build();

        var token = issuer.Token(jwt =>
        {
            jwt.Subject = "ada";
            jwt.Grants = ["notes:read", "notes:write"];
            jwt.Claims["tenant"] = "acme";
        });

        var caller = await validator.Validate(token, Context());

        Assert.NotNull(caller);
        Assert.True(caller.IsAuthenticated);
        Assert.Equal(JwtBearerValidator.SchemeName, caller.AuthenticationScheme);
        Assert.Equal("ada", caller.Subject);
        Assert.Equal(TestJwtIssuer.DefaultIssuer, caller.Issuer);
        Assert.Equal(["notes:read", "notes:write"], caller.Grants.Order(StringComparer.Ordinal));
        Assert.True(caller.TryGetClaim("tenant", out var tenant));
        Assert.Equal("acme", tenant);
    }

    /// <summary>
    /// An array claim, as <c>roles</c> usually is, gives one grant per element.
    /// </summary>
    [Fact]
    public async Task AnArrayGrantClaimGivesAGrantPerElement()
    {
        var (validator, issuer) = Build(grantClaim: "roles");

        var token = issuer.Token(jwt => jwt.Claims["roles"] = new[] { "admin", "editor" });

        var caller = await validator.Validate(token, Context());

        Assert.Equal(["admin", "editor"], caller!.Grants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ATokenWithNoSubjectHasNoSubject()
    {
        var (validator, issuer) = Build();

        var caller = await validator.Validate(issuer.Token(jwt => jwt.Subject = null), Context());

        Assert.True(caller!.IsAuthenticated);
        Assert.Null(caller.Subject);
    }

    [Fact]
    public async Task AnExpiredTokenIsToldWhenItExpired()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt => jwt.Expires = DateTime.UtcNow.AddMinutes(-10))
        );

        Assert.StartsWith("The token expired at ", rejected.RejectionDescription);
    }

    /// <summary>
    /// The configured skew covers clocks that disagree a little.
    /// </summary>
    [Fact]
    public async Task ATokenExpiredWithinTheSkewIsAccepted()
    {
        var (validator, issuer) = Build();

        var caller = await validator.Validate(
            issuer.Token(jwt => jwt.Expires = DateTime.UtcNow.AddSeconds(-20)),
            Context()
        );

        Assert.True(caller!.IsAuthenticated);
    }

    [Fact]
    public async Task ATokenNotYetValidIsToldWhenItWillBe()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt => jwt.NotBefore = DateTime.UtcNow.AddMinutes(10))
        );

        Assert.StartsWith("The token is not valid until ", rejected.RejectionDescription);
    }

    [Fact]
    public async Task ATokenValidFromAfterItExpiresIsInvalid()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt =>
            {
                jwt.Expires = DateTime.UtcNow.AddHours(1);
                jwt.NotBefore = DateTime.UtcNow.AddHours(2);
            })
        );

        Assert.Equal("The token is invalid.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task AnotherIssuersTokenIsRefused()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt => jwt.Issuer = "https://someone-else.test")
        );

        Assert.Equal("The token's issuer is not accepted.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task ATokenForAnotherAudienceIsRefused()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt => jwt.Audience = "someone-else")
        );

        Assert.Equal("The token is not for this audience.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task ATokenSignedByAKeyNotTrustedIsRefused()
    {
        var (validator, issuer) = Build();

        var rejected = await Rejected(
            validator,
            issuer.Token(jwt => jwt.SignedByUntrustedKey = true)
        );

        Assert.Equal("No known key signed the token.", rejected.RejectionDescription);
    }

    /// <summary>
    /// A shared secret cannot say who signed, so an HMAC token is refused whatever it claims. One
    /// whose <c>kid</c> names the trusted RSA key is the algorithm confusion attack. That key does
    /// not verify it, so it is told its signature is invalid.
    /// </summary>
    [Theory]
    [InlineData(null, "No known key signed the token.")]
    [InlineData("hardened-test", "The token's signature is invalid.")]
    public async Task AnHmacSignedTokenIsRefused(string? keyId, string description)
    {
        var (validator, _) = Build();

        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = TestJwtIssuer.DefaultIssuer,
                Audience = TestJwtIssuer.DefaultAudience,
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64)))
                    {
                        KeyId = keyId,
                    },
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );

        var rejected = await Rejected(validator, token);

        Assert.Equal(description, rejected.RejectionDescription);
    }

    /// <summary>
    /// A token that names the trusted key but was signed by another is told its signature is
    /// invalid, rather than that no known key signed it.
    /// </summary>
    [Fact]
    public async Task ATokenNamingATrustedKeyItWasNotSignedWithIsRefused()
    {
        var (validator, _) = Build();
        using var impostor = RSA.Create(2048);

        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = TestJwtIssuer.DefaultIssuer,
                Audience = TestJwtIssuer.DefaultAudience,
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new RsaSecurityKey(impostor) { KeyId = "hardened-test" },
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );

        var rejected = await Rejected(validator, token);

        Assert.Equal("The token's signature is invalid.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task AnUnsignedTokenIsRefused()
    {
        var (validator, _) = Build();

        var token = new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = TestJwtIssuer.DefaultIssuer,
                Audience = TestJwtIssuer.DefaultAudience,
                Expires = DateTime.UtcNow.AddHours(1),
            }
        );

        var rejected = await Rejected(validator, token);

        Assert.Equal("The token's signature is invalid.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task ATokenWithNoExpiryIsRefused()
    {
        var (validator, _) = Build();

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = TestJwtIssuer.DefaultIssuer,
                Audience = TestJwtIssuer.DefaultAudience,
                SigningCredentials = new SigningCredentials(
                    (
                        await new TestJwtIssuer(Configuration()).GetKeys(
                            null,
                            TestContext.Current.CancellationToken
                        )
                    )[0],
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );

        var rejected = await Rejected(validator, token);

        Assert.Equal("The token has no expiry.", rejected.RejectionDescription);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.b")]
    public async Task SomethingThatIsNotAJwtIsRefused(string token)
    {
        var (validator, _) = Build();

        var rejected = await Rejected(validator, token);

        Assert.Equal("The token is not a JWT.", rejected.RejectionDescription);
    }

    /// <summary>
    /// Shaped like a JWT, three segments of URL-safe characters, but its header does not decode.
    /// </summary>
    [Fact]
    public async Task ATokenThatDoesNotDecodeIsMalformed()
    {
        var (validator, _) = Build();

        var rejected = await Rejected(validator, "a.b.c");

        Assert.Equal("The token is malformed.", rejected.RejectionDescription);
    }

    [Fact]
    public async Task TheKeySourceIsAskedForTheTokensKey()
    {
        var configuration = Configuration();
        var issuer = new TestJwtIssuer(configuration);
        var keys = Substitute.For<IJwtSigningKeySource>();

        keys.GetKeys(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => issuer.GetKeys(null, TestContext.Current.CancellationToken));

        await new JwtBearerValidator(configuration, keys).Validate(issuer.Token(), Context());

        await keys.Received(1).GetKeys("hardened-test", Arg.Any<CancellationToken>());
    }
}
