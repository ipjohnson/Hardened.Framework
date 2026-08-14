using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.OpenApi.BuildTask.Validation;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

/// <summary>
/// One schema, as a record or an enum.
/// </summary>
/// <remarks>
/// Both kinds arrive as a <see cref="SchemaModel"/>, so the dispatch belongs here rather than in
/// every caller. These were two emitters when each one produced a file of its own; the split was
/// along output files rather than along input.
/// </remarks>
internal static class SchemaEmitter {

    /// <summary>
    /// Adds the schema's type to <paramref name="container"/> and returns it, so the caller can
    /// decide anything that is not the type's own business - see <see cref="Coverage"/>.
    /// </summary>
    public static IOutputComponent? Emit(
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns) =>
        schema.Kind switch {
            SchemaKind.Object => EmitRecord(container, schema, modelsNamespace, patterns),
            SchemaKind.Enum => EmitEnum(container, schema),
            _ => null,
        };

    /// <summary>
    /// A positional record, or a declaration-only one when the schema carries no properties.
    /// </summary>
    private static ClassDefinition EmitRecord(
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns) {
        var record = container.AddClass(NamingHelper.ToPascalCase(schema.Name));

        record.TypeKeyword = ClassKeyword.Record;
        record.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        record.TerminateWithSemicolon = true;

        // No properties means no parameter list at all - "record Empty;" rather than "record
        // Empty();". The two are different declarations, and the second gives the type a constructor
        // the spec did not ask for.
        if (schema.Properties.Count == 0) {
            return record;
        }

        var constructor = record.AddConstructor();
        constructor.IsPrimary = true;

        // Required parameters must precede optional ones in a C# parameter list.
        foreach (var property in schema.Properties.OrderByDescending(property => property.IsRequired)) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);
            var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, !property.IsRequired);

            var parameter = constructor.AddParameter(
                typeDefinition, NamingHelper.ToPascalCase(property.Name));

            if (!property.IsRequired) {
                parameter.DefaultValue = new CodeOutputComponent("default") { Indented = false };
            }

            EmitJsonPropertyName(parameter, property);

            // property:, because a positional record's parameter and the property it declares are
            // one syntactic position. Without the target the attribute stays on the parameter, where
            // a generator reading properties never sees it - which is what VM0051 warns about.
            // Required, except where the type already guarantees it - see
            // TypeMapper.IsNonNullableValueType.
            var emitRequired = property.IsRequired && !TypeMapper.IsNonNullableValueType(csType);

            foreach (var constraint in ConstraintAttributes.ForProperty(property, emitRequired, patterns)) {
                ValidationEmitter.Apply(parameter, constraint).Target = "property";
            }
        }

        return record;
    }

    /// <summary>
    /// Pins the wire name to what the spec said, rather than to whatever a naming policy makes of
    /// the C# name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted for every property, not only where the two differ. The name in the document is the
    /// contract; deriving it from the C# identifier means the contract depends on a
    /// <c>JsonSerializerOptions</c> setting an application is free to change.
    /// </para>
    /// <para>
    /// The two serialization paths disagreed without it. <c>JsonTypeInfoEmitter</c> writes
    /// <c>PropertyName</c> straight from the spec, so the AOT resolver was always right, while
    /// <c>SystemTextJsonResponseSerializer</c> runs reflection over <c>JsonSerializerDefaults.Web</c>
    /// and camel-cases the C# name. A spec property <c>random_number</c> became <c>RandomNumber</c>,
    /// which the resolver reported as <c>random_number</c> and reflection as <c>randomNumber</c> -
    /// one spec, two wire formats, decided by which serializer an application happened to register.
    /// Nothing caught it because every spec in this repository is already camelCase, where the round
    /// trip is lossless.
    /// </para>
    /// </remarks>
    private static void EmitJsonPropertyName(BaseOutputComponent parameter, PropertyModel property) {
        parameter.AddAttribute(
            TypeDefinition.Get("System.Text.Json.Serialization", "JsonPropertyNameAttribute"),
            new CodeOutputComponent($"\"{property.Name}\"") { Indented = false }).Target = "property";
    }

    private static EnumDefinition EmitEnum(IConstructContainer container, SchemaModel schema) {
        var enumDefinition = container.AddEnum(NamingHelper.ToPascalCase(schema.Name));

        enumDefinition.Modifiers |= ComponentModifier.Public;

        // The wire values are the spec's; the member names are C#. The converter is what keeps the
        // two in step, so it is not optional decoration.
        enumDefinition.AddAttribute(
            TypeDefinition.Get("System.Text.Json.Serialization", "JsonConverterAttribute"),
            new CodeOutputComponent(
                "typeof(System.Text.Json.Serialization.JsonStringEnumConverter)") { Indented = false });

        foreach (var value in schema.EnumValues) {
            enumDefinition.AddValue(NamingHelper.ToPascalCase(value));
        }

        return enumDefinition;
    }
}
