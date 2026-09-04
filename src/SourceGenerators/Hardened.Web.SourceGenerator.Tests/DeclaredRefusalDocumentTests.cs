using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The statuses a guard on an operation contributes to the published document.
/// </summary>
/// <remarks>
/// <para>
/// A filter that can refuse a request answers a status the handler's return type says nothing
/// about, so an operation guarded by one used to publish a document that was true about its success
/// and silent about its refusal. A generated client then had no case for the answer it would
/// actually be sent, which is the gap the 0.19 report recorded for the 403 and the 429.
/// </para>
/// <para>
/// Every assertion here is against the served document, decompressed out of the generated source,
/// rather than against the emitted characters.
/// </para>
/// </remarks>
public class DeclaredRefusalDocumentTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                 // Hardened.Web.Runtime
        typeof(FromBodyAttribute),            // Hardened.Requests.Abstract
        typeof(AuthorizeGrantsAttribute),     // Hardened.Requests.Runtime
        typeof(EnableAttribute<>),            // Hardened.Shared.Runtime
        typeof(OpenApiDocumentPublishing)     // the marker
    ];

    /// <summary>An assembly attribute has to follow the usings and precede the types.</summary>
    private const string Usings = """
        using Hardened.Requests.Abstract.Authorization;
        using Hardened.Requests.Runtime.Authorization;
        using Hardened.Requests.Runtime.Filters;
        using Hardened.Requests.Runtime.RateLimiting;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using Hardened.Web.Runtime.OpenApi;

        """;

    private const string Entrypoint = """
        namespace TestApp;

        [HardenedModule]
        [Enable<OpenApiDocumentPublishing>]
        public partial class Application { }

        """;

    private static JsonElement Document(string controllers, string assemblyLevel = "") {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = Usings + assemblyLevel + Entrypoint + controllers
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors).AssertNoErrors();

        var match = Regex.Match(
            result.SourceContaining("OpenApiDocument"),
            @"new byte\[\]\s*\{(.*?)\}\s*;", RegexOptions.Singleline);

        Assert.True(match.Success, "No document byte array in the generated source.");

        var bytes = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(byte.Parse)
            .ToArray();

        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var inflated = new MemoryStream();

        gzip.CopyTo(inflated);

        return JsonDocument.Parse(inflated.ToArray()).RootElement.Clone();
    }

    private static string[] Statuses(JsonElement document, string path) =>
        document.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToArray();

    private static JsonElement Response(JsonElement document, string path, string status) =>
        document.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("responses").GetProperty(status);

    /// <summary>
    /// The success survives. A guard names a failure and says nothing about what the handler
    /// answers when it runs, so it cannot be the whole set - the same rule
    /// <c>[Throws&lt;T&gt;]</c> follows.
    /// </summary>
    [Fact]
    public void AGuardedOperationStillPublishesItsSuccess() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                [AuthorizeGrants("rates:read")]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "403"], Statuses(document, "/rates"));
    }

    [Fact]
    public void AnAuthorizedOperationPublishesA403WithTheErrorEnvelope() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                [AuthorizeGrants("rates:read")]
                public string Read() => "";
            }
            """);

        var forbidden = Response(document, "/rates", "403");

        Assert.Equal(
            "#/components/schemas/ErrorModel",
            forbidden.GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString());
    }

    [Fact]
    public void ARateLimitedOperationPublishesA429() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                [RateLimit(PermitLimit = 10)]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "429"], Statuses(document, "/rates"));
    }

    [Fact]
    public void ABoundedOperationPublishesA504() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                [Timeout(Milliseconds = 2000)]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "504"], Statuses(document, "/rates"));
    }

    /// <summary>
    /// An operation shedding load answers 503 and never 504, so the document follows what was
    /// written rather than the declaration's default.
    /// </summary>
    [Fact]
    public void ADeclaredStatusReplacesTheDefaultRatherThanJoiningIt() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                [Timeout(Milliseconds = 2000, Status = 503)]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "503"], Statuses(document, "/rates"));
    }

    /// <summary>A controller's guard covers every method on it.</summary>
    [Fact]
    public void AClassLevelGuardReachesItsMethods() {
        var document = Document("""
            [AuthorizeGrants("rates:read")]
            public class RateController {
                [Get("/rates")]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "403"], Statuses(document, "/rates"));
    }

    /// <summary>
    /// A deadline written once beside a library bounds every handler in it, so every one of them
    /// can answer 504 and the document says so.
    /// </summary>
    [Fact]
    public void AnAssemblyLevelDeclarationReachesEveryOperation() {
        var document = Document(
            """
            public class RateController {
                [Get("/rates")]
                public string Read() => "";
            }
            """,
            assemblyLevel: "[assembly: Timeout(Milliseconds = 2000)]\n\n");

        Assert.Equal(["200", "504"], Statuses(document, "/rates"));
    }

    /// <summary>
    /// The nearer declaration decides. An operation answering 503 must not also publish the 504 its
    /// assembly would otherwise have contributed, because it can never answer both.
    /// </summary>
    [Fact]
    public void ANearerDeclarationSupersedesTheAssemblysRatherThanAddingToIt() {
        var document = Document(
            """
            public class RateController {
                [Get("/rates")]
                [Timeout(Milliseconds = 500, Status = 503)]
                public string Read() => "";
            }
            """,
            assemblyLevel: "[assembly: Timeout(Milliseconds = 2000)]\n\n");

        Assert.Equal(["200", "503"], Statuses(document, "/rates"));
    }

    /// <summary>
    /// The extensibility that keeps the document generator ignorant of what a filter does. This
    /// attribute is the application's own and the generator has never heard of it; it publishes a
    /// 403 because <c>IAuthorizeAttribute</c> carries the declaration.
    /// </summary>
    [Fact]
    public void AnApplicationsOwnGuardPublishesTheSameRefusal() {
        var document = Document("""
            public sealed class OwnedByCallerAttribute : System.Attribute, IAuthorizeAttribute {
                public Requirement Requirement { get; } =
                    Requirement.Predicate((_, _) => true, "the caller owns this record");
            }

            public class RateController {
                [Get("/rates")]
                [OwnedByCaller]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200", "403"], Statuses(document, "/rates"));
    }

    /// <summary>An operation nothing guards publishes what it always did.</summary>
    [Fact]
    public void AnUnguardedOperationPublishesOnlyItsSuccess() {
        var document = Document("""
            public class RateController {
                [Get("/rates")]
                public string Read() => "";
            }
            """);

        Assert.Equal(["200"], Statuses(document, "/rates"));
    }
}
