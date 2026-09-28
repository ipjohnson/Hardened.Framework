using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Tests.Support;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Authorization;

/// <summary>
/// The shipped bearer source: which requests it reads, what it hands the validator, and what a
/// refused token becomes.
/// </summary>
public class BearerPrincipalSourceTests
{
    private sealed class ApiBearer : IAuthenticationScheme;

    private static readonly ICallerPrincipal Ada = new CallerPrincipal("bearer", subject: "ada");

    /// <summary>A validator that records the tokens it was given and answers <paramref name="answer"/>.</summary>
    private sealed class Validator(ICallerPrincipal? answer)
    {
        public List<string> Tokens { get; } = [];

        public ValueTask<ICallerPrincipal?> Validate(string token, IExecutionContext context)
        {
            Tokens.Add(token);

            return new ValueTask<ICallerPrincipal?>(answer);
        }
    }

    private static async Task<(ICallerPrincipal? Principal, Validator Validator)> Authenticate(
        string? authorization,
        ICallerPrincipal? answer = null
    )
    {
        var context = Pipeline.Context();

        if (authorization != null)
        {
            context.Request.Headers[KnownHeaders.Authorization] = authorization;
        }

        var validator = new Validator(answer);

        var principal = await new BearerPrincipalSource<ApiBearer>(validator.Validate).Authenticate(
            context
        );

        return (principal, validator);
    }

    [Fact]
    public async Task AValidTokenIsTheValidatorsPrincipal()
    {
        var (principal, validator) = await Authenticate("Bearer abc.def", Ada);

        Assert.Same(Ada, principal);
        Assert.Equal(["abc.def"], validator.Tokens);
    }

    /// <summary>
    /// RFC 9110 compares an authentication scheme without case.
    /// </summary>
    [Theory]
    [InlineData("bearer abc")]
    [InlineData("BEARER abc")]
    [InlineData("  Bearer   abc  ")]
    public async Task TheSchemeWordIsReadWithoutCaseAndTheTokenTrimmed(string authorization)
    {
        var (_, validator) = await Authenticate(authorization, Ada);

        Assert.Equal(["abc"], validator.Tokens);
    }

    /// <summary>
    /// A refused token is the source's to answer. The request continues anonymously, and a
    /// requirement answers it with <c>error="invalid_token"</c>.
    /// </summary>
    [Fact]
    public async Task ARefusedTokenIsARejectedAnonymousCaller()
    {
        var (principal, _) = await Authenticate("Bearer expired", answer: null);

        Assert.False(principal!.IsAuthenticated);

        var rejected = Assert.IsType<AnonymousCallerPrincipal>(principal);

        Assert.True(rejected.CredentialRejected);
        Assert.Null(rejected.RejectionDescription);
    }

    [Fact]
    public async Task AValidatorThatSaysWhyKeepsItsReason()
    {
        var (principal, _) = await Authenticate(
            "Bearer expired",
            AnonymousCallerPrincipal.Rejected("The token expired.")
        );

        var rejected = Assert.IsType<AnonymousCallerPrincipal>(principal);

        Assert.Equal("The token expired.", rejected.RejectionDescription);
    }

    /// <summary>
    /// A request carrying no bearer token is not this source's, so the next source is asked and
    /// the validator is not called.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Basic YWRhOnNlY3JldA==")]
    [InlineData("Bearer")]
    [InlineData("Bearer   ")]
    [InlineData("Bearerabc")]
    public async Task ARequestWithoutABearerTokenIsNotThisSources(string? authorization)
    {
        var (principal, validator) = await Authenticate(authorization, Ada);

        Assert.Null(principal);
        Assert.Empty(validator.Tokens);
    }
}
