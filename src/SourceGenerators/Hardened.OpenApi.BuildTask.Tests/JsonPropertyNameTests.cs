using System.Collections.Generic;
using System.Linq;
using Hardened.OpenApi.BuildTask.Validation;
using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The wire name a generated model serialises under.
/// </summary>
/// <remarks>
/// It has to come from the spec rather than from the C# identifier, because the two serialization
/// paths derive it differently: <c>JsonTypeInfoEmitter</c> writes <c>PropertyName</c> straight from
/// the document, while the reflection serializer camel-cases the property name under
/// <c>JsonSerializerDefaults.Web</c>. Anything that does not survive that round trip produced one
/// wire format under AOT and another under reflection.
/// </remarks>
public class JsonPropertyNameTests {

    private static string Emit(params string[] propertyNames) {
        var schema = new SchemaModel { Name = "Thing", Kind = SchemaKind.Object };

        foreach (var name in propertyNames) {
            schema.Properties.Add(new PropertyModel { Name = name, Type = "string", IsRequired = true });
        }

        var patterns = new PatternRegistry(EmitterHarness.RootNamespace + ".Validation", "spec");

        return EmitterHarness.Write(ns =>
            SchemaEmitter.Emit(ns, schema, EmitterHarness.ModelsNamespace, patterns));
    }

    /// <summary>
    /// The case that was silently wrong. PascalCase turns snake_case into RandomNumber, which
    /// reflection then writes as "randomNumber" while the resolver writes "random_number".
    /// </summary>
    [Fact]
    public void ASnakeCasePropertyKeepsItsSpecName() {
        Assert.Contains("JsonPropertyName(\"random_number\")", Emit("random_number"));
    }

    /// <summary>
    /// Emitted even where camelCase would have produced the same answer. The document is the
    /// contract, and leaving it implicit makes it depend on a JsonSerializerOptions setting an
    /// application can change.
    /// </summary>
    [Fact]
    public void ACamelCasePropertyIsStillPinned() {
        Assert.Contains("JsonPropertyName(\"message\")", Emit("message"));
    }

    [Fact]
    public void TheAttributeTargetsThePropertyRatherThanTheParameter() {
        // A positional record's parameter and its property are one syntactic position, so without
        // the target the attribute lands on the parameter where the serializer never sees it.
        Assert.Contains("[property: JsonPropertyName(\"message\")]", Emit("message"));
    }

    [Fact]
    public void EveryPropertyIsPinned() {
        var source = Emit("id", "random_number");

        Assert.Contains("JsonPropertyName(\"id\")", source);
        Assert.Contains("JsonPropertyName(\"random_number\")", source);
    }
}
