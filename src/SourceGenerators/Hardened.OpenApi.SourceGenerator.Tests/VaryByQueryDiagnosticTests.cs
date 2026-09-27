using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// A <c>VaryByQuery</c> key on a described operation that names no query parameter the contract
/// declares.
/// </summary>
/// <remarks>
/// The front end the 0.41 trial found it in: the contract declares the query parameters, the
/// implementation restates them by hand in the cache key, and a misspelt one built clean.
/// </remarks>
public class VaryByQueryDiagnosticTests
{
    private const string Spec = """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: category
                  in: query
                  schema: { type: string }
                - name: cursor
                  in: query
                  schema: { type: string }
              responses:
                '200': { description: ok }
        """;

    private static string Host(string keys) =>
        $$"""
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Runtime.Caching;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Caching;
            using TestNamespace.Services;

            namespace TestNamespace;

            [HardenedModule]
            public partial class TestApp {
            }

            [Handler]
            public class ThingServiceImpl : IThingService {
                [CacheResponse<VaryByQuery>({{keys}})]
                public Task ListThings(string? category, string? cursor) => Task.CompletedTask;
            }
            """;

    private static IReadOnlyList<Diagnostic> Reported(string keys) =>
        OpenApiGenerator
            .Run(Spec, Host(keys))
            .GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == "HRDW009")
            .ToList();

    [Fact]
    public void AKeyTheContractDoesNotDeclareIsHRDW009()
    {
        var diagnostic = Assert.Single(Reported("\"category\", \"cursr\", Duration = 10"));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'cursr'", diagnostic.GetMessage());
        Assert.Contains("'category', 'cursor'", diagnostic.GetMessage());
    }

    [Fact]
    public void KeysTheContractDeclaresReportNothing()
    {
        Assert.Empty(Reported("\"category\", \"cursor\""));
    }

    [Fact]
    public void NoKeysReportsNothing()
    {
        Assert.Empty(Reported("Duration = 10"));
    }
}
