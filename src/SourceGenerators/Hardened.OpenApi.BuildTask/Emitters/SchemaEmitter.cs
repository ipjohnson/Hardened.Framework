using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
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
    public static IOutputComponent? Emit(IConstructContainer container, SchemaModel schema, string modelsNamespace) =>
        schema.Kind switch {
            SchemaKind.Object => EmitRecord(container, schema, modelsNamespace),
            SchemaKind.Enum => EmitEnum(container, schema),
            _ => null,
        };

    /// <summary>
    /// A positional record, or a declaration-only one when the schema carries no properties.
    /// </summary>
    private static ClassDefinition EmitRecord(
        IConstructContainer container, SchemaModel schema, string modelsNamespace) {
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
        }

        return record;
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
