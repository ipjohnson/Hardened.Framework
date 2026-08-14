using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>in: cookie</c>, which the parser recorded faithfully and every downstream stage then dropped.
/// </summary>
public class CookieParameterTests {

    private const string Spec =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: session
                  in: cookie
                  required: true
                  schema: { type: string }
                - name: theme
                  in: cookie
                  schema: { type: string }
              responses:
                '200': { description: ok }
        """;

    [Fact]
    public void ACookieParameterReachesTheSignature() {
        var generated = OpenApiGenerator.Run(Spec).AssertNoErrors().SourceContaining("petstore.g.cs");

        Assert.Contains("ListThings(string session, string? theme)", generated);
    }

    [Fact]
    public void ACookieParameterIsBoundFromTheCookies() {
        var handler = OpenApiGenerator.Run(Spec).AssertNoErrors()
            .SourceContaining("ThingController_ListThings");

        Assert.Contains("Cookies", handler);
        Assert.Contains("\"session\"", handler);
    }

    [Fact]
    public void AHandlerCanReceiveACookie() {
        OpenApiGenerator.Run(
                Spec,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class ThingServiceImpl : IThingService {
                        public Task ListThings(string session, string? theme) =>
                            Task.FromResult(session + theme);
                    }
                    """))
            .AssertNoErrors();
    }
}
