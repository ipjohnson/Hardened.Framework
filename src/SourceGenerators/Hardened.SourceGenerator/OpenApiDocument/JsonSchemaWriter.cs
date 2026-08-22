using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
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
    /// <param name="compilationAssembly">
    /// The assembly being compiled, which decides which enums this application owns - see
    /// <c>EnumWireNaming.IsOwned</c>.
    /// <para>
    /// The compilation's assembly rather than the root type's. A handler whose response is itself a
    /// framework type anchors the walk in that framework's assembly, and every enum below it then
    /// looks locally declared - which is how <c>System.Reflection.MethodImplAttributes</c> and
    /// <c>TaskStatus</c> acquired generated converters renaming their members.
    /// </para>
    /// </param>
    public static HandlerSchema? Write(ITypeSymbol? type, IAssemblySymbol? compilationAssembly = null) {
        if (type == null || type.SpecialType == SpecialType.System_Void) {
            return null;
        }

        var components = new Dictionary<string, string>();
        var enums = new Dictionary<string, EnumVocabulary>(System.StringComparer.Ordinal);

        var root = SchemaFor(
            Unwrap(type), components, new HashSet<string>(), enums, compilationAssembly);

        return new HandlerSchema(
            root,
            components
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => new SchemaComponent(pair.Key, pair.Value))
                .ToList(),
            enums
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList());
    }

    /// <summary>
    /// The type a handler actually produces. <c>Task&lt;T&gt;</c> is how it is returned, not what it
    /// is - and the handler model records the wrapper rather than the result, so unwrapping here is
    /// what keeps a document from describing every response as a task.
    ///
    /// <para>
    /// <c>IAsyncEnumerable&lt;T&gt;</c> unwraps for a different reason: the response is many of them
    /// rather than one, and what the document needs is the shape of an item. Since OpenAPI 3.2 that
    /// is spelled <c>itemSchema</c>, which is where the caller puts it.
    /// </para>
    /// </summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type) {
        while (type is INamedTypeSymbol { IsGenericType: true } named) {
            var name = named.ConstructedFrom.Name;

            // SseItem<T> alongside the awaitables, because it is a wrapper in the same sense: the
            // wire carries T under data:, and the id and event name sit beside the payload rather
            // than inside it. Documenting SseItem<T> would describe a shape no client ever parses.
            if (name != "Task" && name != "ValueTask" &&
                name != "IAsyncEnumerable" && name != "SseItem") {
                break;
            }

            type = named.TypeArguments[0];
        }

        return type;
    }

    private static string SchemaFor(
        ITypeSymbol type, Dictionary<string, string> components, HashSet<string> inProgress,
        Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
        if (type is INamedTypeSymbol { IsGenericType: true } nullable &&
            nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T) {
            return SchemaFor(nullable.TypeArguments[0], components, inProgress, enums, compilationAssembly);
        }

        var primitive = Primitive(type);

        if (primitive != null) {
            return primitive;
        }

        if (type is IArrayTypeSymbol array) {
            return "{\"type\":\"array\",\"items\":" +
                   SchemaFor(array.ElementType, components, inProgress, enums, compilationAssembly) + "}";
        }

        if (type is INamedTypeSymbol named) {
            var collection = Collection(named, components, inProgress, enums, compilationAssembly);

            if (collection != null) {
                return collection;
            }

            if (named.TypeKind == TypeKind.Enum) {
                return EnumSchema(named, enums, compilationAssembly);
            }

            if (named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface) {
                return ObjectRef(named, components, inProgress, enums, compilationAssembly);
            }
        }

        // Nothing better to say about it than that it is a value.
        return "{}";
    }

    private static string? Collection(
        INamedTypeSymbol named, Dictionary<string, string> components, HashSet<string> inProgress,
        Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
        if (!named.IsGenericType) {
            return null;
        }

        var name = named.ConstructedFrom.Name;

        if (name is "List" or "IList" or "IReadOnlyList" or "ICollection" or "IReadOnlyCollection"
            or "IEnumerable" or "HashSet" or "ISet") {
            return "{\"type\":\"array\",\"items\":" +
                   SchemaFor(named.TypeArguments[0], components, inProgress, enums, compilationAssembly) + "}";
        }

        if (name is "Dictionary" or "IDictionary" or "IReadOnlyDictionary") {
            return "{\"type\":\"object\",\"additionalProperties\":" +
                   SchemaFor(named.TypeArguments[1], components, inProgress, enums, compilationAssembly) + "}";
        }

        return null;
    }

    /// <summary>
    /// The enum's declared values, in the vocabulary the serializer will actually write.
    /// </summary>
    /// <remarks>
    /// This used to write <c>member.Name</c> unconditionally, and the JSON serializer wrote the
    /// ordinal - so the published description said <c>{"type":"string","enum":["ScienceFiction"]}</c>
    /// about a property that went out as <c>0</c>. The document is the deliverable here, and a
    /// client generated from it could not talk to the application it was generated from.
    ///
    /// Resolved through <see cref="EnumWireNaming"/> rather than formatted here, because the
    /// converter and the parameter binder resolve the same way from the same place. A document that
    /// disagrees with the wire is the defect; two implementations of one policy is how it returns.
    /// </remarks>
    private static string EnumSchema(
        INamedTypeSymbol named, Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
        var owned = EnumWireNaming.IsOwned(named, compilationAssembly);

        // An enum the application does not own keeps the member name it always had here, and gets
        // no converter. A model graph reaches further than it looks - a property typed Exception
        // pulls in System.Reflection.MethodAttributes - and renaming those is redefining a
        // vocabulary that is not the application's to redefine.
        var naming = owned
            ? EnumWireNaming.For(named, EnumWireNaming.AssemblyDefault(named))
            : "MemberName";

        var members = EnumWireNaming.Members(named, naming);
        var qualified = "global::" + named.ToDisplayString();

        // Recorded whether or not it is new: the same enum reached from two handlers resolves to the
        // same vocabulary, and the dictionary is what keeps one converter emitted for it.
        if (owned && members.Count > 0) {
            enums[qualified] = new EnumVocabulary(
                qualified,
                named.Name,
                naming,
                members.Select(pair => new EnumWireValue(pair.Member, pair.Wire)).ToList());
        }

        var builder = new StringBuilder("{\"type\":\"string\",\"enum\":[");
        var first = true;

        foreach (var (_, wire) in members) {
            if (!first) {
                builder.Append(',');
            }

            builder.Append('"').Append(Escape(wire)).Append('"');
            first = false;
        }

        return builder.Append("]}").ToString();
    }

    private static string ObjectRef(
        INamedTypeSymbol named, Dictionary<string, string> components, HashSet<string> inProgress,
        Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
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
                    SchemaFor(property.Type, components, inProgress, enums, compilationAssembly), property));

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
