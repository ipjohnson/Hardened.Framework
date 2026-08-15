using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.OpenApiDocument;

/// <summary>
/// A C# type as JSON Schema, together with every named type it reaches.
/// </summary>
/// <remarks>
/// <para>
/// Runs inside the syntax transform, which is the only place a Roslyn symbol still exists. The
/// handler model that survives the transform holds <c>ITypeDefinition</c> - a namespace and a name -
/// so a type's members are unreachable by the time the document is written. Converting here and
/// carrying the result forward is what makes the reverse direction possible at all.
/// </para>
/// <para>
/// Named types become entries in <c>components/schemas</c> and are referenced by <c>$ref</c>, so a
/// type reaching itself terminates instead of expanding forever - and a type used by several
/// operations is written once.
/// </para>
/// </remarks>
public static class JsonSchemaWriter {

    /// <summary>
    /// The schema for <paramref name="type"/>, and every named schema it depends on.
    /// </summary>
    public static HandlerSchema? Write(ITypeSymbol? type) {
        if (type == null || type.SpecialType == SpecialType.System_Void) {
            return null;
        }

        var components = new Dictionary<string, string>();

        var root = SchemaFor(Unwrap(type), components, new HashSet<string>());

        return new HandlerSchema(
            root,
            components
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => new SchemaComponent(pair.Key, pair.Value))
                .ToList());
    }

    /// <summary>
    /// The type a handler actually produces. <c>Task&lt;T&gt;</c> is how it is returned, not what it
    /// is - and the handler model records the wrapper rather than the result, so unwrapping here is
    /// what keeps a document from describing every response as a task.
    /// </summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type) {
        while (type is INamedTypeSymbol { IsGenericType: true } named) {
            var name = named.ConstructedFrom.Name;

            if (name != "Task" && name != "ValueTask" && name != "IAsyncEnumerable") {
                break;
            }

            type = named.TypeArguments[0];
        }

        return type;
    }

    private static string SchemaFor(
        ITypeSymbol type, Dictionary<string, string> components, HashSet<string> inProgress) {
        if (type is INamedTypeSymbol { IsGenericType: true } nullable &&
            nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T) {
            return SchemaFor(nullable.TypeArguments[0], components, inProgress);
        }

        var primitive = Primitive(type);

        if (primitive != null) {
            return primitive;
        }

        if (type is IArrayTypeSymbol array) {
            return "{\"type\":\"array\",\"items\":" +
                   SchemaFor(array.ElementType, components, inProgress) + "}";
        }

        if (type is INamedTypeSymbol named) {
            var collection = Collection(named, components, inProgress);

            if (collection != null) {
                return collection;
            }

            if (named.TypeKind == TypeKind.Enum) {
                return EnumSchema(named);
            }

            if (named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface) {
                return ObjectRef(named, components, inProgress);
            }
        }

        // Nothing better to say about it than that it is a value.
        return "{}";
    }

    private static string? Collection(
        INamedTypeSymbol named, Dictionary<string, string> components, HashSet<string> inProgress) {
        if (!named.IsGenericType) {
            return null;
        }

        var name = named.ConstructedFrom.Name;

        if (name is "List" or "IList" or "IReadOnlyList" or "ICollection" or "IReadOnlyCollection"
            or "IEnumerable" or "HashSet" or "ISet") {
            return "{\"type\":\"array\",\"items\":" +
                   SchemaFor(named.TypeArguments[0], components, inProgress) + "}";
        }

        if (name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary") {
            return "{\"type\":\"object\",\"additionalProperties\":" +
                   SchemaFor(named.TypeArguments[1], components, inProgress) + "}";
        }

        return null;
    }

    private static string EnumSchema(INamedTypeSymbol named) {
        var builder = new StringBuilder("{\"type\":\"string\",\"enum\":[");
        var first = true;

        foreach (var member in named.GetMembers().OfType<IFieldSymbol>()) {
            if (!member.IsConst) {
                continue;
            }

            if (!first) {
                builder.Append(',');
            }

            builder.Append('"').Append(Escape(member.Name)).Append('"');
            first = false;
        }

        return builder.Append("]}").ToString();
    }

    private static string ObjectRef(
        INamedTypeSymbol named, Dictionary<string, string> components, HashSet<string> inProgress) {
        var name = named.Name;
        var reference = "{\"$ref\":\"#/components/schemas/" + Escape(name) + "\"}";

        // Already written, or being written further up the stack - a type reaching itself.
        if (components.ContainsKey(name) || !inProgress.Add(name)) {
            return reference;
        }

        var properties = new StringBuilder();
        var required = new List<string>();
        var first = true;

        foreach (var property in named.GetMembers().OfType<IPropertySymbol>()) {
            if (property.DeclaredAccessibility != Accessibility.Public ||
                property.IsStatic ||
                property.GetMethod == null) {
                continue;
            }

            if (!first) {
                properties.Append(',');
            }

            properties
                .Append('"').Append(Escape(CamelCase(property.Name))).Append("\":")
                .Append(SchemaConstraintWriter.Apply(
                    SchemaFor(property.Type, components, inProgress), property));

            // A non-nullable reference type is one the author said would always be there, and so
            // is one carrying [Required] - which is the only way to say it about a value type.
            if ((property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                 property.Type.IsReferenceType) ||
                SchemaConstraintWriter.IsRequired(property)) {
                required.Add(CamelCase(property.Name));
            }

            first = false;
        }

        var schema = new StringBuilder("{\"type\":\"object\"");

        if (required.Count > 0) {
            schema.Append(",\"required\":[")
                .Append(string.Join(",", required.Select(r => "\"" + Escape(r) + "\"")))
                .Append(']');
        }

        schema.Append(",\"properties\":{").Append(properties).Append("}}");

        components[name] = schema.ToString();

        inProgress.Remove(name);

        return reference;
    }

    private static string? Primitive(ITypeSymbol type) =>
        type.SpecialType switch {
            SpecialType.System_String or SpecialType.System_Char => "{\"type\":\"string\"}",
            SpecialType.System_Boolean => "{\"type\":\"boolean\"}",
            SpecialType.System_Byte or SpecialType.System_SByte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 =>
                "{\"type\":\"integer\",\"format\":\"int32\"}",
            SpecialType.System_Int64 or SpecialType.System_UInt64 =>
                "{\"type\":\"integer\",\"format\":\"int64\"}",
            SpecialType.System_Single => "{\"type\":\"number\",\"format\":\"float\"}",
            SpecialType.System_Double => "{\"type\":\"number\",\"format\":\"double\"}",
            SpecialType.System_Decimal => "{\"type\":\"number\"}",
            SpecialType.System_DateTime => "{\"type\":\"string\",\"format\":\"date-time\"}",
            SpecialType.System_Object => "{}",
            _ => ByName(type)
        };

    private static string? ByName(ITypeSymbol type) =>
        type.Name switch {
            "Guid" => "{\"type\":\"string\",\"format\":\"uuid\"}",
            "DateOnly" => "{\"type\":\"string\",\"format\":\"date\"}",
            "TimeOnly" or "TimeSpan" => "{\"type\":\"string\"}",
            "DateTimeOffset" => "{\"type\":\"string\",\"format\":\"date-time\"}",
            "Uri" => "{\"type\":\"string\",\"format\":\"uri\"}",
            _ => null
        };

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

    internal static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
