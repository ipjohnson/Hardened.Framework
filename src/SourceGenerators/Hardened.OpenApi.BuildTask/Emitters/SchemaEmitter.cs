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
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns,
        IReadOnlyList<SchemaModel>? allSchemas = null) =>
        schema.Kind switch {
            SchemaKind.Object => EmitRecord(container, schema, modelsNamespace, patterns, allSchemas),
            SchemaKind.Enum => EmitEnum(container, schema),
            _ => null,
        };

    /// <summary>
    /// A schema's properties in the order its record declares them.
    /// </summary>
    /// <remarks>
    /// Required first, because C# will not take an optional parameter before a required one. A
    /// derived record has to pass its base's arguments in this same order, which is why it is
    /// computed once here rather than at each call site.
    /// </remarks>
    private static IEnumerable<PropertyModel> InDeclarationOrder(SchemaModel schema) =>
        schema.Properties.OrderByDescending(property => property.IsRequired);

    /// <summary>
    /// A positional record, or a declaration-only one when the schema carries no properties.
    /// </summary>
    private static ClassDefinition EmitRecord(
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns,
        IReadOnlyList<SchemaModel>? allSchemas) {
        var record = container.AddClass(NamingHelper.ToPascalCase(schema.Name));

        record.TypeKeyword = ClassKeyword.Record;
        record.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        record.TerminateWithSemicolon = true;
        record.Comment = DocComment.Format(schema.Description);

        if (schema.IsDeprecated) {
            Deprecation.Apply(record);
        }

        EmitBaseType(record, schema, modelsNamespace, allSchemas);

        // No properties means no parameter list at all - "record Empty;" rather than "record
        // Empty();". The two are different declarations, and the second gives the type a constructor
        // the spec did not ask for.
        if (schema.Properties.Count == 0) {
            return record;
        }

        var constructor = record.AddConstructor();
        constructor.IsPrimary = true;

        // Required parameters must precede optional ones in a C# parameter list.
        foreach (var property in InDeclarationOrder(schema)) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);
            var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, property.IsCSharpNullable);

            var parameter = constructor.AddParameter(
                typeDefinition, NamingHelper.ToPascalCase(property.Name));

            parameter.Comment = DocComment.Format(property.Description);

            if (property.HasDefault) {
                // The spec's own default where it has a constant form, and the type's otherwise.
                var literal = DefaultLiteral.Format(property.Default, csType) ?? "default";

                parameter.DefaultValue = new CodeOutputComponent(literal) { Indented = false };
            }

            EmitJsonPropertyName(parameter, property);

            // property:, because a positional record's parameter and the property it declares are
            // one syntactic position. Without the target the attribute stays on the parameter, where
            // a generator reading properties never sees it - which is what VM0051 warns about.
            // Required, except where the type already guarantees it - see
            // TypeMapper.IsNonNullableValueType.
            var emitRequired = property.ConstrainedAsRequired && !TypeMapper.IsNonNullableValueType(csType);

            foreach (var constraint in ConstraintAttributes.ForProperty(property, emitRequired, patterns)) {
                ValidationEmitter.Apply(parameter, constraint).Target = "property";
            }
        }

        return record;
    }

    /// <summary>
    /// The base a derived record inherits, and the arguments it passes to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A derived schema carries its base's properties as well as its own - <c>allOf</c> merges them
    /// - so the record declares all of them positionally and forwards the base's share. That is the
    /// standard shape for record inheritance: <c>record Dog(string PetType, string Name, string
    /// Breed) : Pet(PetType, Name)</c> declares <c>Breed</c> and inherits the other two rather than
    /// redeclaring them.
    /// </para>
    /// <para>
    /// The arguments are ordered by the base's declaration order, not this schema's. The two agree
    /// on membership but not necessarily on order, since each sorts its own required properties
    /// first.
    /// </para>
    /// </remarks>
    private static void EmitBaseType(
        ClassDefinition record, SchemaModel schema, string modelsNamespace,
        IReadOnlyList<SchemaModel>? allSchemas) {
        if (schema.BaseRef == null) {
            return;
        }

        var baseName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(schema.BaseRef));
        var baseType = TypeDefinition.Get(modelsNamespace, baseName);

        var baseSchema = allSchemas?.FirstOrDefault(
            candidate => NamingHelper.ToPascalCase(candidate.Name) == baseName);

        if (baseSchema == null || baseSchema.Properties.Count == 0) {
            record.AddBaseType(baseType);

            return;
        }

        var arguments = new List<IOutputComponent>();

        foreach (var property in InDeclarationOrder(baseSchema)) {
            arguments.Add(
                new CodeOutputComponent(NamingHelper.ToPascalCase(property.Name)) { Indented = false });
        }

        record.AddBaseType(baseType, arguments.ToArray());
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
        enumDefinition.Comment = DocComment.Format(schema.Description);

        if (schema.IsDeprecated) {
            Deprecation.Apply(enumDefinition);
        }

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
