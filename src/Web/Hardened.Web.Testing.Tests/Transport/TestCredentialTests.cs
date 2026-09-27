using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Testing.Tests.Transport;

/// <summary>
/// Which credential wins: the parameter's, then the method's, then the class's, then the
/// assembly's, with <see cref="AnonymousAttribute"/> cancelling whatever was wider.
/// </summary>
public class TestCredentialTests
{
    [Grants("class:grant")]
    [Subject("class-subject")]
    private sealed class Fixture
    {
        public void Bare(object client) { }

        [Grants("method:grant")]
        public void OnMethod(object client, [Grants("parameter:grant")] object attributed) { }

        [Anonymous]
        public void Cancelled(object client, [Grants("parameter:grant")] object attributed) { }

        [Subject("method-subject")]
        public void SubjectOnly([Anonymous] object nobody) { }
    }

    /// <summary>The runner's view of a method: attributes widest first, as ITestMethodContext promises.</summary>
    private sealed class Context : ITestMethodContext
    {
        public Context(string method, params Attribute[] assemblyAttributes)
        {
            Method = typeof(Fixture).GetMethod(method)!;
            Attributes = assemblyAttributes
                .Concat(typeof(Fixture).GetCustomAttributes())
                .Concat(Method.GetCustomAttributes())
                .ToList();
        }

        public MethodInfo Method { get; }

        public IReadOnlyList<Attribute> Attributes { get; }
    }

    private static ParameterInfo Parameter(string method, int index) =>
        typeof(Fixture).GetMethod(method)!.GetParameters()[index];

    [Fact]
    public void TheClassAppliesWhenTheMethodSaysNothing()
    {
        var credential = TestCredential.Resolve(new Context("Bare"));

        Assert.Equal(new[] { "class:grant" }, credential.Grants);
        Assert.Equal("class-subject", credential.Subject);
    }

    [Fact]
    public void TheClassBeatsTheAssembly()
    {
        var credential = TestCredential.Resolve(
            new Context("Bare", new GrantsAttribute("assembly:grant"))
        );

        Assert.Equal(new[] { "class:grant" }, credential.Grants);
    }

    [Fact]
    public void TheAssemblyAppliesWhenNothingNarrowerSpeaks()
    {
        var credential = TestCredential.Resolve(new[] { new GrantsAttribute("assembly:grant") });

        Assert.Equal(new[] { "assembly:grant" }, credential.Grants);
        Assert.Null(credential.Subject);
    }

    [Fact]
    public void TheMethodBeatsTheClassAndKeepsItsSubject()
    {
        var credential = TestCredential.Resolve(new Context("OnMethod"));

        Assert.Equal(new[] { "method:grant" }, credential.Grants);
        Assert.Equal("class-subject", credential.Subject);
    }

    [Fact]
    public void TheParameterBeatsTheMethod()
    {
        var credential = TestCredential.Resolve(new Context("OnMethod"), Parameter("OnMethod", 1));

        Assert.Equal(new[] { "parameter:grant" }, credential.Grants);
    }

    [Fact]
    public void ABareParameterTakesTheMethods()
    {
        var credential = TestCredential.Resolve(new Context("OnMethod"), Parameter("OnMethod", 0));

        Assert.Equal(new[] { "method:grant" }, credential.Grants);
    }

    [Fact]
    public void AnonymousOnTheMethodCancelsTheClass()
    {
        var credential = TestCredential.Resolve(new Context("Cancelled"));

        Assert.True(credential.IsAnonymous);
        Assert.Null(credential.Subject);
    }

    [Fact]
    public void AParameterGrantAppliesOverAnAnonymousMethod()
    {
        var credential = TestCredential.Resolve(
            new Context("Cancelled"),
            Parameter("Cancelled", 1)
        );

        Assert.Equal(new[] { "parameter:grant" }, credential.Grants);
    }

    [Fact]
    public void AnonymousOnAParameterCancelsEverything()
    {
        var credential = TestCredential.Resolve(
            new Context("SubjectOnly"),
            Parameter("SubjectOnly", 0)
        );

        Assert.True(credential.IsAnonymous);
    }

    /// <summary>A subject with no grants is still a known caller, which the source spells "-".</summary>
    [Fact]
    public void ASubjectAloneIsAnAuthenticatedCallerHoldingNothing()
    {
        var credential = new TestCredential(null, "pia");
        var headers = new Dictionary<string, StringValues>();

        credential.ApplyTo(headers);

        Assert.Equal("-", headers["X-Test-Grants"].ToString());
        Assert.Equal("pia", headers["X-Test-Subject"].ToString());
    }

    [Fact]
    public void AnAnonymousCredentialSetsNoHeaders()
    {
        var headers = new Dictionary<string, StringValues>();

        TestCredential.Anonymous.ApplyTo(headers);

        Assert.Empty(headers);
    }

    [Fact]
    public void ACallerWhoSetEitherHeaderKeepsBoth()
    {
        var credential = new TestCredential(new[] { "a" }, "pia");
        var headers = new Dictionary<string, StringValues> { ["X-Test-Subject"] = "someone-else" };

        credential.ApplyTo(headers);

        Assert.False(headers.ContainsKey("X-Test-Grants"));
        Assert.Equal("someone-else", headers["X-Test-Subject"].ToString());
    }

    /// <summary>
    /// On a socket host the credential rides in a handler, applied to each request that carries
    /// neither header: both headers for a subject with grants, the grants alone otherwise.
    /// </summary>
    [Fact]
    public void ASocketRequestCarryingNeitherHeaderGetsBoth()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/pets");

        new TestCredential(new[] { "pets:read" }, "pia").ApplyTo(request);

        Assert.Equal(
            "pets:read",
            request
                .Headers.GetValues(Requests.Testing.TestGrantsPrincipalSource.GrantsHeader)
                .Single()
        );
        Assert.Equal(
            "pia",
            request
                .Headers.GetValues(Requests.Testing.TestGrantsPrincipalSource.SubjectHeader)
                .Single()
        );
    }

    [Fact]
    public void ASocketRequestWithNoSubjectGetsTheGrantsAlone()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/pets");

        new TestCredential(new[] { "pets:read" }).ApplyTo(request);

        Assert.True(
            request.Headers.Contains(Requests.Testing.TestGrantsPrincipalSource.GrantsHeader)
        );
        Assert.False(
            request.Headers.Contains(Requests.Testing.TestGrantsPrincipalSource.SubjectHeader)
        );
    }

    [Fact]
    public void ASocketRequestThatSetItsOwnSubjectKeepsIt()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/pets");
        request.Headers.Add(
            Requests.Testing.TestGrantsPrincipalSource.SubjectHeader,
            "someone-else"
        );

        new TestCredential(new[] { "pets:read" }, "pia").ApplyTo(request);

        Assert.Equal(
            "someone-else",
            request
                .Headers.GetValues(Requests.Testing.TestGrantsPrincipalSource.SubjectHeader)
                .Single()
        );
        Assert.False(
            request.Headers.Contains(Requests.Testing.TestGrantsPrincipalSource.GrantsHeader)
        );
    }

    [Fact]
    public void AnAnonymousCredentialLeavesASocketRequestAlone()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/pets");

        TestCredential.Anonymous.ApplyTo(request);

        Assert.Empty(request.Headers);
    }

    [Fact]
    public void TheHeadersBecomeTheClientsDefaults()
    {
        using var client = new HttpClient();

        new TestCredential(new[] { "a", "b" }, "pia").ApplyTo(client);

        Assert.Equal("a b", client.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
        Assert.Equal("pia", client.DefaultRequestHeaders.GetValues("X-Test-Subject").Single());
    }

    [Claim("tenant", "class-tenant")]
    [Claim("region", "eu")]
    private sealed class ClaimsFixture
    {
        [Claim("tenant", "method-tenant")]
        [Grants("method:grant")]
        public void OnMethod() { }

        [Anonymous]
        public void Cancelled() { }
    }

    private static TestCredential ResolveClaims(string method) =>
        TestCredential.Resolve(
            typeof(ClaimsFixture)
                .GetCustomAttributes()
                .Concat(typeof(ClaimsFixture).GetMethod(method)!.GetCustomAttributes())
        );

    /// <summary>
    /// Claims gather across the levels, and a narrower level's value replaces a wider one's for the
    /// same name. A grant beside them leaves them alone.
    /// </summary>
    [Fact]
    public void ANarrowerClaimReplacesTheSameNameAndKeepsTheOthers()
    {
        var credential = ResolveClaims(nameof(ClaimsFixture.OnMethod));

        Assert.Equal(new[] { "method:grant" }, credential.Grants);
        Assert.Equal("method-tenant", credential.Claims!["tenant"]);
        Assert.Equal("eu", credential.Claims["region"]);
    }

    [Fact]
    public void AnonymousClearsTheClaims()
    {
        Assert.True(ResolveClaims(nameof(ClaimsFixture.Cancelled)).IsAnonymous);
    }

    /// <summary>
    /// A claim alone is an authenticated caller holding no grants, and a value holding the header's
    /// own separators reaches the principal whole.
    /// </summary>
    [Fact]
    public async Task AClaimReachesThePrincipalWhole()
    {
        var credential = TestCredential.Resolve(
            new[] { new ClaimAttribute("tenant", "a&b=c, d"), new ClaimAttribute("empty", "") }
        );

        var request = PipelineRequest.CreateRequest(
            "GET",
            "/",
            new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase),
            Stream.Null,
            credential
        );

        var context = Substitute.For<IExecutionContext>();

        context.Request.Returns(request);

        var caller = await new TestGrantsPrincipalSource().Authenticate(context);

        Assert.NotNull(caller);
        Assert.Empty(caller.Grants);
        Assert.True(caller.TryGetClaim("tenant", out var tenant));
        Assert.Equal("a&b=c, d", tenant);
        Assert.True(caller.TryGetClaim("empty", out var empty));
        Assert.Equal("", empty);
    }

    [Fact]
    public void TheClaimsBecomeTheClientsDefaults()
    {
        using var client = new HttpClient();

        (
            new TestCredential(null)
            {
                Claims = new Dictionary<string, string> { ["tenant"] = "acme" },
            }
        ).ApplyTo(client);

        Assert.Equal("-", client.DefaultRequestHeaders.GetValues("X-Test-Grants").Single());
        Assert.Equal(
            "tenant=acme",
            client.DefaultRequestHeaders.GetValues("X-Test-Claims").Single()
        );
    }
}
