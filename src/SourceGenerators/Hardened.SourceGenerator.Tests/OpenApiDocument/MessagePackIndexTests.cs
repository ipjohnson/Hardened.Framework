using System.Linq;
using System.Text.Json;
using Hardened.SourceGenerator.OpenApiDocument;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Hardened.SourceGenerator.Tests.OpenApiDocument;

/// <summary>
/// The MessagePack key a code-first model carries, published into the document.
/// </summary>
/// <remarks>
/// <para>
/// Hardened does not put <c>[Key(0)]</c> on a code-first model and cannot: a source generator has
/// no way to add an attribute to a member of a type it did not declare. The author writes it, and
/// what this does is carry it - so the published document says the same thing about field identity
/// that a contract stating <c>x-message-pack-index</c> would, and a client generated from either
/// agrees with the server.
/// </para>
/// <para>
/// The fixture declares its own <c>MessagePack.KeyAttribute</c> rather than referencing the
/// package, the way the response-set fixtures declare their own <c>[HttpStatus]</c>: what is being
/// tested is the rule that reads the attribute, not the package that ships it.
/// </para>
/// </remarks>
public class MessagePackIndexTests {

    /// <summary>
    /// The two attributes the fixtures name, declared rather than referenced. The compilation
    /// carries only System.Private.CoreLib, so an unresolved <c>[JsonPropertyName]</c> would bind
    /// to nothing and the member would quietly publish under its camel-cased name - which is what
    /// the last test here is asserting is not what happens.
    /// </summary>
    private const string Attributes =
        """
        namespace MessagePack {
            public class KeyAttribute : System.Attribute {
                public KeyAttribute(int x) { }
                public KeyAttribute(string x) { }
            }
        }

        namespace System.Text.Json.Serialization {
            public class JsonPropertyNameAttribute : System.Attribute {
                public JsonPropertyNameAttribute(string name) { }
            }
        }
        """;

    private static JsonElement Properties(string declaration, string typeName = "Reading") {
        var tree = CSharpSyntaxTree.ParseText(Attributes + "\n" + declaration);

        var compilation = CSharpCompilation.Create(
            "MessagePackFixture",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var type = compilation.GetTypeByMetadataName("App." + typeName);

        Assert.NotNull(type);

        var written = JsonSchemaWriter.Write(type, compilation.Assembly);

        Assert.NotNull(written);

        var component = written!.Components.Single(c => c.Name == typeName);

        return JsonDocument.Parse(component.Json).RootElement.GetProperty("properties");
    }

    /// <summary>
    /// The attribute on a positional record parameter reaches the property it declares, which is
    /// where this reads it - the same position <c>[JsonPropertyName]</c> is read from.
    /// </summary>
    [Fact]
    public void AKeyedRecordPublishesEveryIndex() {
        var properties = Properties(
            """
            namespace App {
                using MessagePack;

                public record Reading([property: Key(0)] string Sensor, [property: Key(1)] int Value);
            }
            """);

        Assert.Equal(0, properties.GetProperty("sensor").GetProperty("x-message-pack-index").GetInt32());
        Assert.Equal(1, properties.GetProperty("value").GetProperty("x-message-pack-index").GetInt32());
    }

    /// <summary>
    /// A model nobody keyed publishes the document it always did. Nothing is invented for it, here
    /// or anywhere: an index Hardened chose would describe a wire format the server does not speak.
    /// </summary>
    [Fact]
    public void AnUnkeyedModelPublishesNoExtension() {
        var properties = Properties(
            """
            namespace App {
                public record Reading(string Sensor, int Value);
            }
            """);

        Assert.False(properties.GetProperty("sensor").TryGetProperty("x-message-pack-index", out _));
    }

    /// <summary>
    /// <c>[Key("sensor")]</c> renames a member under the named mode and states no index, so there
    /// is nothing to publish. The document's own property name already carries that answer.
    /// </summary>
    [Fact]
    public void TheStringOverloadIsNotAnIndex() {
        var properties = Properties(
            """
            namespace App {
                using MessagePack;

                public record Reading([property: Key("s")] string Sensor);
            }
            """);

        Assert.False(properties.GetProperty("sensor").TryGetProperty("x-message-pack-index", out _));
    }

    /// <summary>
    /// A key on a member that is also renamed for the wire lands under the name the wire carries,
    /// because the two answers describe the same member and the document indexes it by name.
    /// </summary>
    [Fact]
    public void TheIndexLandsUnderTheWireName() {
        var properties = Properties(
            """
            namespace App {
                using MessagePack;
                using System.Text.Json.Serialization;

                public record Reading([property: JsonPropertyName("sensor_id"), Key(4)] string Sensor);
            }
            """);

        Assert.Equal(
            4, properties.GetProperty("sensor_id").GetProperty("x-message-pack-index").GetInt32());
    }
}
