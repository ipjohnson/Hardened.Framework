using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What <c>x-hardened-request-interfaces</c> puts on an operation's generated <c>Parameters</c>
/// class, and what it refuses.
/// </summary>
public class DescribedRequestInterfaceTests
{
    private const string Source = """
        using Hardened.Shared.Runtime.Attributes;

        namespace TestNamespace
        {
            [HardenedModule]
            public partial class TestApp { }
        }

        namespace Shop.Api
        {
            public interface ITenantRequest
            {
                string tenant { get; }
            }

            public interface IOrderRequest
            {
                string order { get; }
            }

            public interface IPascalTenantRequest
            {
                string Tenant { get; }
            }
        }
        """;

    private const string TenantInterface = "global::Shop.Api.ITenantRequest";

    private const string OrderInterface = "global::Shop.Api.IOrderRequest";

    private static string Spec(string tenantSchema = "{ type: string }") =>
        $$"""
            openapi: "3.0.0"
            info: { title: Shop, version: "1.0" }
            x-hardened-request-interfaces:
              - Shop.Api.ITenantRequest
            paths:
              /tenants/{tenant}/orders:
                get:
                  tags: [Order]
                  operationId: listOrders
                  parameters:
                    - name: tenant
                      in: path
                      required: true
                      schema: {{tenantSchema}}
                  responses:
                    '200':
                      description: Listed
                      content:
                        application/json:
                          schema: { type: string }
              /tenants/{tenant}/orders/{order}:
                x-hardened-request-interfaces:
                  - Shop.Api.IOrderRequest
                get:
                  tags: [Order]
                  operationId: getOrder
                  x-hardened-request-interfaces:
                    - Shop.Api.ITenantRequest
                  parameters:
                    - name: tenant
                      in: path
                      required: true
                      schema: { type: string }
                    - name: order
                      in: path
                      required: true
                      schema: { type: string }
                  responses:
                    '200':
                      description: Found
                      content:
                        application/json:
                          schema: { type: string }
              /health:
                get:
                  tags: [Order]
                  operationId: health
                  x-hardened-request-interfaces: []
                  parameters:
                    - { name: verbose, in: query, schema: { type: boolean } }
                  responses:
                    '200':
                      description: Healthy
                      content:
                        application/json:
                          schema: { type: string }
            """;

    private static GeneratorResult Run(string spec) => OpenApiGenerator.Run(spec, Source);

    /// <summary>The base list of the <c>Parameters</c> class generated for one operation.</summary>
    private static string BaseList(GeneratorResult result, string operation)
    {
        var source = result
            .GeneratedSources.Where(pair => pair.Key.Contains(operation))
            .Select(pair => pair.Value)
            .Single(value => value.Contains("class Parameters"));

        var start = source.IndexOf("class Parameters", StringComparison.Ordinal);

        return source.Substring(start, source.IndexOf('\n', start) - start);
    }

    private static string Hoag033(GeneratorResult result) =>
        string.Join(
            "\n",
            result.GeneratorDiagnostics.Where(d => d.Id == "HOAG033").Select(d => d.GetMessage())
        );

    [Fact]
    public void TheDocumentsListReachesEveryOperationThatDoesNotClearIt()
    {
        var result = Run(Spec()).AssertNoErrors();

        Assert.Contains(TenantInterface, BaseList(result, "ListOrders"));
        Assert.DoesNotContain(OrderInterface, BaseList(result, "ListOrders"));
    }

    /// <summary>
    /// The path item adds to the document's list, and the operation naming one the document
    /// already gave does not list it twice.
    /// </summary>
    [Fact]
    public void ThePathItemAndTheOperationAddToTheDocumentsList()
    {
        var baseList = BaseList(Run(Spec()).AssertNoErrors(), "GetOrder");

        Assert.Contains(TenantInterface, baseList);
        Assert.Contains(OrderInterface, baseList);
        Assert.Equal(1, baseList.Split(TenantInterface, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AnEmptyListOnAnOperationClearsWhatItInherits()
    {
        var baseList = BaseList(Run(Spec()).AssertNoErrors(), "Health");

        Assert.DoesNotContain("Shop.Api", baseList);
    }

    [Fact]
    public void TheParserResolvesTheListWidestFirst()
    {
        var model = OpenApiSpecParser.Parse(Spec(), "shop", CancellationToken.None)!;
        var operations = model
            .Services.SelectMany(s => s.Operations)
            .ToDictionary(o => o.OperationId);

        Assert.Equal(["Shop.Api.ITenantRequest"], operations["listOrders"].RequestInterfaces);
        Assert.Equal(
            ["Shop.Api.ITenantRequest", "Shop.Api.IOrderRequest"],
            operations["getOrder"].RequestInterfaces
        );
        Assert.Empty(operations["health"].RequestInterfaces);
    }

    /// <summary>
    /// A constrained parameter gives the class the validation interface, and the named interface
    /// joins it rather than replacing it.
    /// </summary>
    [Fact]
    public void TheValidationInterfaceStaysOnTheBaseList()
    {
        var baseList = BaseList(
            Run(Spec("{ type: string, minLength: 3 }")).AssertNoErrors(),
            "ListOrders"
        );

        Assert.Contains("ExecutionRequestParameters", baseList);
        Assert.Contains("Validation.", baseList);
        Assert.Contains(TenantInterface, baseList);
    }

    [Fact]
    public void TheServedDocumentRepeatsTheListOnEachOperation()
    {
        var paths = SpecFirstDocumentTests.PublishedDocumentFrom(Run(Spec())).GetProperty("paths");

        string[] Names(string path) =>
            paths
                .GetProperty(path)
                .GetProperty("get")
                .GetProperty("x-hardened-request-interfaces")
                .EnumerateArray()
                .Select(name => name.GetString()!)
                .ToArray();

        Assert.Equal(["Shop.Api.ITenantRequest"], Names("/tenants/{tenant}/orders"));
        Assert.Equal(
            ["Shop.Api.ITenantRequest", "Shop.Api.IOrderRequest"],
            Names("/tenants/{tenant}/orders/{order}")
        );
        Assert.False(
            paths
                .GetProperty("/health")
                .GetProperty("get")
                .TryGetProperty("x-hardened-request-interfaces", out _)
        );
    }

    /// <summary>
    /// HOAG033 is the only error, because the interface that does not fit is left off the base
    /// list rather than becoming a CS0535 beside it.
    /// </summary>
    [Fact]
    public void AnOperationLackingAMemberStopsTheBuildNamingTheOperationTheInterfaceAndTheMember()
    {
        var result = Run(
            Spec().Replace("x-hardened-request-interfaces: []", "x-hardened-timeout: 1000")
        );

        var message = Hoag033(result);

        Assert.Contains("'health'", message);
        Assert.Contains("Shop.Api.ITenantRequest", message);
        Assert.Contains("'tenant'", message);
        Assert.Equal(
            DiagnosticSeverityError,
            result.GeneratorDiagnostics.First(d => d.Id == "HOAG033").Severity
        );
        Assert.DoesNotContain(result.CompilationDiagnostics, d => d.Id == "CS0535");
    }

    [Fact]
    public void AMemberNamedInAnotherCaseIsReportedWithThePropertysName()
    {
        var message = Hoag033(
            Run(
                Spec()
                    .Replace(
                        "  - Shop.Api.ITenantRequest\npaths",
                        "  - Shop.Api.IPascalTenantRequest\npaths"
                    )
            )
        );

        Assert.Contains("'listOrders'", message);
        Assert.Contains("'Tenant'", message);
        Assert.Contains("named 'tenant'", message);
    }

    private static string QuerySpec(string tenantParameter) =>
        $$"""
            openapi: "3.0.0"
            info: { title: Shop, version: "1.0" }
            x-hardened-request-interfaces: [Shop.Api.ITenantRequest]
            paths:
              /orders:
                get:
                  tags: [Order]
                  operationId: searchOrders
                  parameters:
                    - {{tenantParameter}}
                  responses:
                    '200':
                      description: Listed
                      content:
                        application/json:
                          schema: { type: string }
            """;

    [Fact]
    public void ARequiredQueryParameterSatisfiesTheMember()
    {
        var result = Run(
                QuerySpec("{ name: tenant, in: query, required: true, schema: { type: string } }")
            )
            .AssertNoErrors();

        Assert.Contains(TenantInterface, BaseList(result, "SearchOrders"));
    }

    /// <summary>
    /// An optional parameter is a nullable property, which cannot implement a getter promising a
    /// string. The compiler says so only as a warning.
    /// </summary>
    [Fact]
    public void ANullablePropertyDoesNotSatisfyANonNullableMember()
    {
        var message = Hoag033(
            Run(QuerySpec("{ name: tenant, in: query, schema: { type: string } }"))
        );

        Assert.Contains("'searchOrders'", message);
        Assert.Contains("can be null", message);
    }

    [Fact]
    public void AnIntegerPropertyDoesNotSatisfyAStringMember()
    {
        var message = Hoag033(Run(Spec("{ type: integer }")));

        Assert.Contains("'listOrders'", message);
        Assert.Contains("int", message);
        Assert.Contains("'tenant'", message);
    }

    [Fact]
    public void ANameTheCompilationDoesNotDeclareIsReported()
    {
        var message = Hoag033(
            Run(Spec().Replace("- Shop.Api.IOrderRequest", "- Shop.Api.IMissingRequest"))
        );

        Assert.Contains("'getOrder'", message);
        Assert.Contains("Shop.Api.IMissingRequest", message);
        Assert.Contains("declares no type of that name", message);
    }

    [Fact]
    public void AListThatIsNotAListIsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            OpenApiSpecParser.Parse(
                Spec()
                    .Replace(
                        "x-hardened-request-interfaces: []",
                        "x-hardened-request-interfaces: Shop"
                    ),
                "shop",
                CancellationToken.None
            )
        );

        Assert.Contains("'health'", exception.Message);
    }

    private const Microsoft.CodeAnalysis.DiagnosticSeverity DiagnosticSeverityError = Microsoft
        .CodeAnalysis
        .DiagnosticSeverity
        .Error;
}
