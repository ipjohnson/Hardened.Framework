using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.Generation;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// An OpenAPI schema, written from the normalised model rather than from a type symbol.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonSchemaWriter"/> does the same job for an attribute-routed application and cannot
/// serve this one: it walks an <c>ITypeSymbol</c>, and a specification-first handler's payload types
/// are written by the build task rather than declared in the consumer's source. The result was a
/// published document with paths and operation ids and nothing else - no request body, no response
/// content, no <c>components</c> at all - because every schema the writer would have produced came
/// from symbols this path does not have.
/// </para>
/// <para>
/// It does have the schemas, in more detail than a symbol carries: the description, the declared
/// constraints and the wire vocabulary of an enum are all in the model, and none of them survive a
/// round trip through C#. So this writes from the model directly, and the document ends up saying
/// what the contract said rather than what the generated C# could be made to admit.
/// </para>
/// </remarks>
internal static class SpecSchemaWriter {

    /// <summary>
    /// The schema for one named component, plus every component it reaches.
    /// </summary>
    /// <summary>
    /// An inline scalar schema, for a success the contract types without naming - a
    /// <c>text/plain</c> string, a bare number. It carried no schema at all, so the document
    /// published the status with no <c>content</c> and a generated client read nothing.
    /// </summary>
    public static HandlerSchema? ForScalar(string? type, string? format) {
        if (string.IsNullOrEmpty(type)) {
            return null;
        }

        var schema = string.IsNullOrEmpty(format)
            ? "{\"type\":\"" + type + "\"}"
            : "{\"type\":\"" + type + "\",\"format\":\"" + format + "\"}";

        return new HandlerSchema(schema, System.Array.Empty<SchemaComponent>());
    }

    public static HandlerSchema? ForRef(string? schemaRef, IReadOnlyList<SchemaModel> schemas) {
        if (schemaRef == null) {
            return null;
        }

        var name = NamingHelper.ToPascalCase(TypeMapper.GetRefName(schemaRef));
        var components = new Dictionary<string, string>();

        Collect(name, schemas, components, new HashSet<string>());

        return components.Count == 0
            ? null
            : new HandlerSchema(Reference(name), Components(components));
    }

    /// <summary>The schema for an array of a named component.</summary>
    public static HandlerSchema? ForArrayOf(string? itemsRef, IReadOnlyList<SchemaModel> schemas) {
        if (itemsRef == null) {
            return null;
        }

        var name = NamingHelper.ToPascalCase(TypeMapper.GetRefName(itemsRef));
        var components = new Dictionary<string, string>();

        Collect(name, schemas, components, new HashSet<string>());

        return components.Count == 0
            ? null
            : new HandlerSchema(
                "{\"type\":\"array\",\"items\":" + Reference(name) + "}", Components(components));
    }

    private static IReadOnlyList<SchemaComponent> Components(Dictionary<string, string> components) {
        var result = new List<SchemaComponent>();

        foreach (var pair in components) {
            result.Add(new SchemaComponent(pair.Key, pair.Value));
        }

        result.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }

    private static string Reference(string name) =>
        "{\"$ref\":\"#/components/schemas/" + name + "\"}";

    /// <summary>
    /// Writes a named schema into <paramref name="components"/>, and everything it references.
    /// </summary>
    /// <remarks>
    /// <paramref name="seen"/> rather than recursion depth, because a model may reference itself -
    /// a tree node with children of its own type is ordinary - and the document expresses that with
    /// a <c>$ref</c> back to the same component rather than by expanding forever.
    /// </remarks>
    private static void Collect(
        string name, IReadOnlyList<SchemaModel> schemas,
        Dictionary<string, string> components, HashSet<string> seen) {
        if (!seen.Add(name)) {
            return;
        }

        SchemaModel? schema = null;

        foreach (var candidate in schemas) {
            if (NamingHelper.ToPascalCase(candidate.Name) == name) {
                schema = candidate;

                break;
            }
        }

        if (schema == null) {
            return;
        }

        components[name] = Write(schema, schemas, components, seen);
    }

    private static string Write(
        SchemaModel schema, IReadOnlyList<SchemaModel> schemas,
        Dictionary<string, string> components, HashSet<string> seen) {
        var builder = new StringBuilder();

        switch (schema.Kind) {
            case SchemaKind.Enum:
                builder.Append("{\"type\":\"string\"");
                Describe(builder, schema.Description);
                builder.Append(",\"enum\":[");

                for (var i = 0; i < schema.EnumValues.Count; i++) {
                    if (i > 0) {
                        builder.Append(',');
                    }

                    builder.Append('"').Append(JsonSchemaWriter.Escape(schema.EnumValues[i])).Append('"');
                }

                builder.Append("]}");

                break;

            case SchemaKind.Array:
                builder.Append("{\"type\":\"array\"");
                Describe(builder, schema.Description);
                builder.Append(",\"items\":")
                    .Append(Inline(schema.ArrayItemsRef, schema.ArrayItemsType, schema.ArrayItemsFormat,
                        null, null, false, schemas, components, seen));

                if (schema.MinItems.HasValue) {
                    builder.Append(",\"minItems\":").Append(schema.MinItems.Value);
                }

                if (schema.MaxItems.HasValue) {
                    builder.Append(",\"maxItems\":").Append(schema.MaxItems.Value);
                }

                builder.Append('}');

                break;

            default:
                builder.Append("{\"type\":\"object\"");
                Describe(builder, schema.Description);
                WriteProperties(builder, schema, schemas, components, seen);
                builder.Append('}');

                break;
        }

        return builder.ToString();
    }

    private static void WriteProperties(
        StringBuilder builder, SchemaModel schema, IReadOnlyList<SchemaModel> schemas,
        Dictionary<string, string> components, HashSet<string> seen) {
        if (schema.Properties.Count == 0) {
            return;
        }

        builder.Append(",\"properties\":{");

        for (var i = 0; i < schema.Properties.Count; i++) {
            var property = schema.Properties[i];

            if (i > 0) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(property.Name)).Append("\":");

            if (property.IsArray) {
                var array = new StringBuilder("{\"type\":\"array\",\"items\":")
                    .Append(Inline(property.ArrayItemsRef, property.ArrayItemsType,
                        property.ArrayItemsFormat, null, null, false, schemas, components, seen));

                if (property.MinItems.HasValue) {
                    array.Append(",\"minItems\":").Append(property.MinItems.Value);
                }

                if (property.MaxItems.HasValue) {
                    array.Append(",\"maxItems\":").Append(property.MaxItems.Value);
                }

                Describe(array, property.Description);

                builder.Append(Nullable(array.Append('}').ToString(), property.IsNullable));
            }
            else {
                builder.Append(Inline(property.Ref, property.Type, property.Format,
                    property.Description, property, property.IsNullable, schemas, components, seen));
            }
        }

        builder.Append('}');

        WriteRequired(builder, schema);
    }

    /// <summary>
    /// The <c>required</c> array, from the members the contract marked required.
    /// </summary>
    /// <remarks>
    /// A member bound to a response header is not in the body at all, so it cannot be required of
    /// one - listing it would describe a payload no service ever sends.
    /// </remarks>
    private static void WriteRequired(StringBuilder builder, SchemaModel schema) {
        var required = new List<string>();

        foreach (var property in schema.Properties) {
            if (property.IsRequired && !property.IsHeaderBound) {
                required.Add(property.Name);
            }
        }

        if (required.Count == 0) {
            return;
        }

        builder.Append(",\"required\":[");

        for (var i = 0; i < required.Count; i++) {
            if (i > 0) {
                builder.Append(',');
            }

            builder.Append('"').Append(JsonSchemaWriter.Escape(required[i])).Append('"');
        }

        builder.Append(']');
    }

    /// <summary>
    /// A property's schema: a reference where it names a component, a scalar where it does not.
    /// </summary>
    /// <remarks>
    /// A <c>$ref</c> beside a description is not legal in OpenAPI 3.0 - the sibling keys are ignored
    /// - so a described reference is wrapped in <c>allOf</c>, which is the spelling every tool
    /// reads. A described scalar carries the description directly, and its declared constraints
    /// with it: the model held every one of them and this writer published none, which is how the
    /// trial's document-fidelity matrix came out inverted - parameters constrained, bodies bare.
    /// </remarks>
    private static string Inline(
        string? schemaRef, string? type, string? format, string? description,
        IConstraintFacets? facets, bool isNullable,
        IReadOnlyList<SchemaModel> schemas, Dictionary<string, string> components,
        HashSet<string> seen) {
        if (schemaRef != null) {
            var name = NamingHelper.ToPascalCase(TypeMapper.GetRefName(schemaRef));

            Collect(name, schemas, components, seen);

            if (string.IsNullOrEmpty(description)) {
                return Reference(name);
            }

            var wrapped = new StringBuilder("{\"allOf\":[").Append(Reference(name)).Append(']');

            Describe(wrapped, description);

            return wrapped.Append('}').ToString();
        }

        var builder = new StringBuilder("{\"type\":\"")
            .Append(JsonSchemaWriter.Escape(type ?? "string")).Append('"');

        if (!string.IsNullOrEmpty(format)) {
            builder.Append(",\"format\":\"").Append(JsonSchemaWriter.Escape(format!)).Append('"');
        }

        if (facets != null) {
            OpenApiDocumentGenerator.AppendConstraintFacets(
                builder, type ?? "string", facets, OpenApiVersionFacts.Default);
        }

        Describe(builder, description);

        return Nullable(builder.Append('}').ToString(), isNullable);
    }

    /// <summary>
    /// The schema with <c>"null"</c> added to its type when the contract marks the member
    /// nullable.
    /// </summary>
    /// <remarks>
    /// The 2020-12 spelling - a type array - ported from <c>JsonSchemaWriter.Nullable</c> and
    /// correct here for the same reason it gives: the default document is one this spelling is
    /// valid in. A <c>$ref</c> is left alone; the referenced schema describes the type.
    /// </remarks>
    private static string Nullable(string schema, bool isNullable) {
        if (!isNullable) {
            return schema;
        }

        const string prefix = "{\"type\":\"";

        if (!schema.StartsWith(prefix, System.StringComparison.Ordinal)) {
            return schema;
        }

        var close = schema.IndexOf('"', prefix.Length);

        if (close < 0) {
            return schema;
        }

        var name = schema.Substring(prefix.Length, close - prefix.Length);

        return "{\"type\":[\"" + name + "\",\"null\"]" + schema.Substring(close + 1);
    }

    private static void Describe(StringBuilder builder, string? description) {
        if (string.IsNullOrEmpty(description)) {
            return;
        }

        builder.Append(",\"description\":\"")
            .Append(JsonSchemaWriter.Escape(description!)).Append('"');
    }

    /// <summary>The status's own wording, where the contract gave the response none.</summary>
    public static string DescriptionFor(string? declared, int status) =>
        string.IsNullOrEmpty(declared)
            ? HttpResponseDescription.For(status)
            : declared!;

    private static string Invariant(int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
