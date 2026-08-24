using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// <c>[Throws&lt;T&gt;]</c> — the declaration for the model that answers an error by throwing.
/// </summary>
/// <remarks>
/// The other two models declare their errors in the return type and the document is written from
/// it. A throw is a statement in a method body and the signature says nothing about it, so it needs
/// somewhere to be written down. What is asserted here is that the declaration is well formed and
/// reaches the document — not that the handler throws it, and not that it throws nothing else. An
/// unmapped exception is unplanned, and the runtime already has somewhere to put it.
/// </remarks>
public class ThrowsAttributeTests {

    private static string Application(string handler) => $$"""
        using System;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Requests.Abstract.Responses;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;
        using Hardened.Web.Runtime.OpenApi;

        namespace TestApp;

        [HardenedModule]
        [Enable<OpenApiDocumentPublishing>]
        public partial class Application { }

        public record Pet(string Id, string Name);

        {{handler}}
        """;

    /// <summary>
    /// The status comes from the type, exactly as a union case's does.
    /// </summary>
    [Fact]
    public void AThrownTypeCarryingItsStatusNeedsNoArgument() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public class PetController {
                [Get("/pets/{id}")]
                [Throws<RateLimited>]
                public Task<Pet> Get(string id) => Task.FromResult(new Pet(id, "x"));
            }
            """));

        result.AssertNoErrors();

        var document = Document(result);

        Assert.Contains("\"429\"", document);
        Assert.Contains("\"200\"", document);
    }

    /// <summary>
    /// Declaring a thrown error must not delete the success the return type states.
    /// </summary>
    /// <remarks>
    /// A Response or union return type declares every status including the success one, so the
    /// declared set is complete. [Throws] declares only failures. Treating both the same way made
    /// one thrown error erase the 200, publishing a contract for a handler that can only fail.
    /// </remarks>
    [Fact]
    public void DeclaringAThrownErrorKeepsTheSuccessResponse() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public class PetController {
                [Get("/pets/{id}")]
                [Throws<NotFound>]
                [Throws<Conflict>]
                public Task<Pet> Get(string id) => Task.FromResult(new Pet(id, "x"));
            }
            """));

        result.AssertNoErrors();

        var document = Document(result);

        Assert.Contains("\"200\"", document);
        Assert.Contains("\"404\"", document);
        Assert.Contains("\"409\"", document);
    }

    /// <summary>
    /// A type carrying no status has to say which one it means.
    /// </summary>
    [Fact]
    public void AThrownTypeWithoutAStatusCanStateOne() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public record OutOfStock(string Sku);

            public class PetController {
                [Get("/pets/{id}")]
                [Throws<OutOfStock>(409)]
                public Task<Pet> Get(string id) => Task.FromResult(new Pet(id, "x"));
            }
            """));

        result.AssertNoErrors();

        Assert.Contains("\"409\"", Document(result));
    }

    /// <summary>
    /// The guarantee: a declaration naming neither a status-carrying type nor a status is refused.
    /// </summary>
    /// <remarks>
    /// Reported rather than dropped. A response the author wrote down and the document does not
    /// carry is worse than one they never wrote — the contract claims the handler cannot answer
    /// something it can.
    /// </remarks>
    [Fact]
    public void AThrownTypeWithNoStatusAnywhereIsRefused() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public record OutOfStock(string Sku);

            public class PetController {
                [Get("/pets/{id}")]
                [Throws<OutOfStock>]
                public Task<Pet> Get(string id) => Task.FromResult(new Pet(id, "x"));
            }
            """));

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "HRDT001");
    }

    /// <summary>
    /// The document itself, not the C# that carries it.
    /// </summary>
    /// <remarks>
    /// It is emitted as a gzipped byte array in a partial class, so asserting against the generated
    /// source searches compressed bytes and finds nothing - which is exactly the false negative this
    /// suite hit first time round.
    /// </remarks>
    private static string Document(GeneratorResult result) {
        var source = result.GeneratedSources
            .First(candidate => candidate.Key.Contains("OpenApiDocument")).Value;

        var start = source.IndexOf("new byte[]", StringComparison.Ordinal);

        Assert.True(start >= 0, "The document source carries no byte array.");

        var bytes = System.Text.RegularExpressions.Regex
            .Matches(source.Substring(start), @"\b\d{1,3}\b")
            .Select(match => byte.Parse(match.Value))
            .ToArray();

        using var compressed = new MemoryStream(bytes);
        using var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        return reader.ReadToEnd();
    }
}
