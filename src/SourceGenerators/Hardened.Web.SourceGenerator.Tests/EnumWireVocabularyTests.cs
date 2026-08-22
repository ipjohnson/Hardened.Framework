using System.Text.Json;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The enum converters this generator writes, through the shipped generator rather than the
/// library it compiles in.
/// </summary>
/// <remarks>
/// The emitter itself is covered in <c>Hardened.SourceGenerator.Tests</c>. Held again here because
/// the wrapper projects compile the library in as linked <c>Compile</c> items rather than
/// referencing it, so the same source is a different assembly in each - and code covered in one
/// wrapper's tests is uncovered in every other. That is a property of the layout, not duplication
/// for its own sake: this suite is what says the generator an application actually loads produces
/// the converters.
/// </remarks>
public class EnumWireVocabularyTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),          // Hardened.Web.Runtime
        typeof(FromBodyAttribute)      // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(source, new WebLibrarySourceGenerator(), Anchors);

    private const string Application = """
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }

        public enum Priority { Low, InProgress }

        [JsonEnumNaming(EnumNaming.KebabCaseLower)]
        public enum Shipping { NextDay }

        public record Ticket(Priority Priority, Shipping Shipping);

        public class TicketController {
            [Get("/tickets")]
            public Ticket Get() => new(Priority.Low, Shipping.NextDay);

            [Get("/by-priority")]
            public string ByPriority([FromQueryString] Priority priority) => priority.ToString();
        }
        """;

    [Fact]
    public void TheGeneratedConvertersCarryTheResolvedVocabulary() {
        var routing = Generate(Application).AssertNoErrors().SourceContaining("Application.Routing");

        Assert.Contains("=> \"inProgress\"", routing);
        Assert.Contains("=> \"next-day\"", routing);
    }

    [Fact]
    public void TheResolverAndStringConvertersAreRegistered() {
        var routing = Generate(Application).AssertNoErrors().SourceContaining("Application.Routing");

        Assert.Contains("JsonEnums.Resolver.Instance", routing);
        Assert.Contains("JsonEnums.StringConverters", routing);
    }

    /// <summary>
    /// The document is written from the same resolution, which is the pairing the whole mechanism
    /// exists for - a description declaring values the wire does not carry is a contract no client
    /// can honour.
    /// </summary>
    [Fact]
    public void TheDocumentDeclaresTheSameValues() {
        var result = Generate($$"""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            {{GeneratedOpenApiDocument.EnableAttribute}}
            public partial class Application { }

            public enum Priority { Low, InProgress }

            public record Ticket(Priority Priority);

            public class TicketController {
                [Get("/tickets")]
                public Ticket Get() => new(Priority.Low);
            }
            """).AssertNoErrors();

        // Selected by hint name, as the other document tests do. Several generated files mention
        // OpenApiDocument; exactly one carries it.
        var source = result.GeneratedSources
            .Single(pair => pair.Key.Contains("OpenApiDocument")).Value;

        using var document = JsonDocument.Parse(GeneratedOpenApiDocument.Extract(source));

        var values = document.RootElement
            .GetProperty("components").GetProperty("schemas")
            .GetProperty("Ticket").GetProperty("properties").GetProperty("priority")
            .GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()).ToArray();

        Assert.Equal(new[] { "low", "inProgress" }, values);
    }
}
