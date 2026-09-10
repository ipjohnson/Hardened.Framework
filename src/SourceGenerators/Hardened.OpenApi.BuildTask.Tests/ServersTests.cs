using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// What a contract's <c>servers</c> block reaches the model as.
/// </summary>
/// <remarks>
/// It was read for its path component and otherwise dropped, so a specification-first document
/// published no <c>servers</c> at all: every path and no host to send one to, with <c>[Server]</c>
/// read off the entry point rather than the contract and so no way to say it from here either.
/// </remarks>
public class ServersTests {

    private static ServiceSpecModel Parse(string yaml, bool applyServerBasePath = false) {
        var model = OpenApiSpecParser.Parse(
            yaml, "spec", CancellationToken.None, applyServerBasePath);

        Assert.NotNull(model);

        return model!;
    }

    private const string Paths = @"
paths:
  /todos:
    get:
      operationId: listTodos
      responses:
        '200':
          description: The todos.
";

    private static string Spec(string servers) =>
        "openapi: \"3.0.0\"\ninfo:\n  title: T\n  version: \"1.0.0\"\n" + servers + Paths;

    [Fact]
    public void EveryServerReachesTheModelInTheOrderItWasWritten() {
        var model = Parse(Spec(@"
servers:
  - url: https://api.example.com
    description: production
  - url: https://staging.example.com
"));

        Assert.Equal(2, model.Servers.Count);
        Assert.Equal("https://api.example.com", model.Servers[0].Url);
        Assert.Equal("production", model.Servers[0].Description);
        Assert.Equal("https://staging.example.com", model.Servers[1].Url);
        Assert.Null(model.Servers[1].Description);
    }

    [Fact]
    public void AContractThatDeclaresNoServersCarriesNone() {
        Assert.Empty(Parse(Spec("")).Servers);
    }

    /// <summary>A trailing slash is not part of the URL a path is joined to.</summary>
    [Fact]
    public void ATrailingSlashIsRemoved() {
        var model = Parse(Spec(@"
servers:
  - url: https://api.example.com/
"));

        Assert.Equal("https://api.example.com", Assert.Single(model.Servers).Url);
    }

    /// <summary>
    /// The doubling the base path option would otherwise cause.
    /// </summary>
    /// <remarks>
    /// With the option on, every route is written under <c>/v1</c> already. Publishing the URL as
    /// the contract wrote it would have a client join <c>https://api.example.com/v1</c> to
    /// <c>/v1/todos</c>, which is the mistake <c>ServerBasePath</c>'s own remarks give as the
    /// reason the option is opt-in in the first place.
    /// </remarks>
    [Fact]
    public void AnAppliedBasePathIsRemovedFromTheServerThatCarriedIt() {
        const string spec = @"
servers:
  - url: https://api.example.com/v1
";

        Assert.Equal(
            "https://api.example.com",
            Assert.Single(Parse(Spec(spec), applyServerBasePath: true).Servers).Url);

        // And with the option off the path is still the server's, because nothing moved it.
        Assert.Equal(
            "https://api.example.com/v1",
            Assert.Single(Parse(Spec(spec)).Servers).Url);
    }

    /// <summary>
    /// Only the suffix that was applied, and only where it is the suffix.
    /// </summary>
    /// <remarks>
    /// The base path comes from the first server alone, so a second server with a path of its own
    /// was never the one written onto the routes. Stripping it too would be a second wrong answer
    /// rather than a fix.
    /// </remarks>
    [Fact]
    public void ASecondServerWithADifferentPathIsLeftAlone() {
        var model = Parse(Spec(@"
servers:
  - url: https://api.example.com/v1
  - url: https://staging.example.com/v2
"), applyServerBasePath: true);

        Assert.Equal("https://api.example.com", model.Servers[0].Url);
        Assert.Equal("https://staging.example.com/v2", model.Servers[1].Url);
    }

    /// <summary>A server that is nothing but the applied base path has nothing left to publish.</summary>
    [Fact]
    public void AServerThatIsOnlyTheBasePathIsDropped() {
        Assert.Empty(Parse(Spec(@"
servers:
  - url: /v1
"), applyServerBasePath: true).Servers);
    }

    /// <summary>
    /// A variable reaches the document as written.
    /// </summary>
    /// <remarks>
    /// <c>ServerBasePath</c> refuses an unresolved <c>{variable}</c> because the route tree
    /// compiles a path into character comparisons and cannot match a brace. A document reader has
    /// no such problem: server variables are part of the specification, so publishing one is
    /// republishing what the author wrote.
    /// </remarks>
    [Fact]
    public void AServerVariableIsPublishedAsWritten() {
        var model = Parse(Spec(@"
servers:
  - url: https://{region}.example.com
    variables:
      region:
        default: eu
"));

        Assert.Equal("https://{region}.example.com", Assert.Single(model.Servers).Url);
    }
}
