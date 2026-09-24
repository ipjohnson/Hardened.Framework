using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A described operation's CORS comes from its implementation, and reaches the table the
/// description generates.
/// </summary>
/// <remarks>
/// The spec table is written by the same routing generator as an attribute-routed one. What differs
/// is where the declaration was read: off the <c>[Handler]</c> class, onto the model the
/// description produced.
/// </remarks>
public class DescribedCorsTests
{
    private const string Spec = """
        openapi: "3.0.0"
        info: { title: Rates, version: "1.0" }
        paths:
          /rates:
            get:
              tags: [Rate]
              operationId: readRates
              responses:
                '200':
                  description: A rate
                  content:
                    application/json:
                      schema: { type: string }
        """;

    private static string SpecRouting(string handlerAttributes) =>
        OpenApiGenerator
            .Run(
                Spec,
                OpenApiGenerator.EntryPointWithHandler(
                    $$"""
                    [Handler]
                    {{handlerAttributes}}
                    public class RateServiceImpl : IRateService {
                        public Task<string> ReadRates() => Task.FromResult("1");
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("SpecRouting");

    [Fact]
    public void AnImplementationDeclaringCorsRegistersTheManifest()
    {
        Assert.Contains("CorsManifest", SpecRouting("[global::Hardened.Web.Runtime.Cors.Cors]"));
    }

    [Fact]
    public void AnImplementationDeclaringNoneRegistersNoManifest()
    {
        Assert.DoesNotContain("CorsManifest", SpecRouting(""));
    }
}
