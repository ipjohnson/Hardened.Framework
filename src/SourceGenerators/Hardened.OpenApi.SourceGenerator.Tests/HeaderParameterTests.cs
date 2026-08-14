using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Header parameters, end to end: on the interface, bound by the handler, and constrained like any
/// other.
/// </summary>
public class HeaderParameterTests {

    private const string Spec =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things/{id}:
            get:
              tags: [Thing]
              operationId: getThing
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: string }
                - name: X-Tenant
                  in: header
                  required: true
                  schema: { type: string, minLength: 2 }
                - name: X-Trace
                  in: header
                  schema: { type: string }
              responses:
                '200': { description: ok }
        """;

    /// <summary>
    /// The whole point: an implementation can now see a header the framework was already
    /// extracting and had nowhere to put.
    /// </summary>
    [Fact]
    public void AHandlerCanReceiveAHeader() {
        OpenApiGenerator.Run(
                Spec,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class ThingServiceImpl : IThingService {
                        public Task GetThing(string id, string xTenant, string? xTrace) =>
                            Task.FromResult(xTenant + xTrace);
                    }
                    """))
            .AssertNoErrors();
    }

    [Fact]
    public void TheHeaderIsBoundFromTheHeaderCollection() {
        var result = OpenApiGenerator.Run(Spec).AssertNoErrors();

        var handler = result.SourceContaining("ThingController_GetThing");

        Assert.Contains("Headers", handler);
        Assert.Contains("\"X-Tenant\"", handler);
    }

    /// <summary>
    /// A constraint on a header is compiled like any other, which it could not be while the
    /// parameter was missing from the interface the validator is typed on.
    /// </summary>
    [Fact]
    public void AConstraintOnAHeaderIsCompiled() {
        var result = OpenApiGenerator.Run(Spec).AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("IGetThingParameters", generated);
        Assert.Contains("StringLength", generated);
    }
}
