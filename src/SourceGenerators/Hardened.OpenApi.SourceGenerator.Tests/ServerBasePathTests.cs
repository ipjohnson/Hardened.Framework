using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The <c>servers</c> entry as a route prefix — opt-in, because applying it unasked would silently
/// double a prefix a gateway already strips.
/// </summary>
public class ServerBasePathTests {

    private static string PathOf(string yaml, bool apply) {
        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None, apply);

        Assert.NotNull(model);

        return model!.Services.First().Operations.Single().Path;
    }

    private const string WithServer =
        """
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        servers:
          - url: https://api.example.com/v1
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              responses:
                '200': { description: ok }
        """;

    [Fact]
    public void TheServerPathIsNotAppliedUnlessAskedFor() {
        Assert.Equal("/things", PathOf(WithServer, apply: false));
    }

    [Fact]
    public void AnAbsoluteServerUrlContributesOnlyItsPath() {
        Assert.Equal("/v1/things", PathOf(WithServer, apply: true));
    }

    [Fact]
    public void ARelativeServerUrlIsUsedAsIs() {
        var yaml = WithServer.Replace("https://api.example.com/v1", "/v2");

        Assert.Equal("/v2/things", PathOf(yaml, apply: true));
    }

    /// <summary>A variable with a declared default is substituted before the path is used.</summary>
    [Fact]
    public void ServerVariablesAreResolvedFromTheirDefaults() {
        var yaml =
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            servers:
              - url: https://api.example.com/{version}
                variables:
                  version:
                    default: v3
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  responses:
                    '200': { description: ok }
            """;

        Assert.Equal("/v3/things", PathOf(yaml, apply: true));
    }

    /// <summary>
    /// A variable with no default is dropped rather than emitted. The route tree compiles a path
    /// into character comparisons and would never match a literal brace.
    /// </summary>
    [Fact]
    public void AnUnresolvedVariableIsDroppedRatherThanEmitted() {
        var yaml =
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            servers:
              - url: https://api.example.com/{version}
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  responses:
                    '200': { description: ok }
            """;

        Assert.Equal("/things", PathOf(yaml, apply: true));
    }

    /// <summary>A server that is just a host contributes nothing.</summary>
    [Fact]
    public void AServerWithNoPathContributesNothing() {
        var yaml = WithServer.Replace("https://api.example.com/v1", "https://api.example.com");

        Assert.Equal("/things", PathOf(yaml, apply: true));
    }

    [Fact]
    public void ASpecWithNoServersIsUnaffected() {
        var yaml = WithServer.Replace("servers:\n  - url: https://api.example.com/v1\n", "");

        Assert.Equal("/things", PathOf(yaml, apply: true));
    }
}
