using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
                return EnumRef(named, components, enums, compilationAssembly);
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
    /// An enum as one component, referenced from every member typed with it.
    /// </summary>
    /// <remarks>
    /// The values were written inline at every use, so a document with three enums carried
    /// thirteen copies and a client generator produced one type per property: Refitter's
    /// <c>JobStatus</c>, <c>JobSummaryStatus</c>, <c>JobEventStatus</c> and <c>Status</c> were one
    /// server enum, and every test that moved a value between models converted by hand. One
    /// component under the enum's name is one generated type. The parameter writer in
    /// <c>OpenApiDocumentGenerator</c> refers to the same component from the vocabulary it holds.
    /// </remarks>
    private static string EnumRef(
        INamedTypeSymbol named, Dictionary<string, string> components,
        Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
        var name = SchemaName(named);

        components[name] = EnumSchema(named, enums, compilationAssembly);

        return "{\"$ref\":\"#/components/schemas/" + Escape(name) + "\"}";
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

    /// <summary>
    /// The <c>&lt;summary&gt;</c> on a type or a property, from wherever it was declared.
    /// </summary>
    /// <remarks>
    /// Through the syntax the symbol came from rather than <c>GetDocumentationCommentXml</c>, which
    /// answers nothing unless the compilation was parsed with <c>DocumentationMode.Parse</c> - the
    /// same dependency that kept every handler's prose out of the document until
    /// <see cref="XmlDocumentation"/> learned to read raw trivia. A symbol from another assembly has
    /// no syntax here and contributes nothing, which is correct: its prose is in its own document.
    /// </remarks>
    private static string? DocumentationOf(ISymbol symbol) {
        foreach (var reference in symbol.DeclaringSyntaxReferences) {
            var summary = XmlDocumentation.Read(reference.GetSyntax()).Summary;

            if (summary != null) {
                return summary;
            }
        }

        return null;
    }

    /// <summary>Adds a description to a schema that has been written already.</summary>
    private static string Describe(string schema, string? description) =>
        description == null
            ? schema
            : Append(schema, "\"description\":\"" + Escape(description) + "\"");

    /// <summary>Adds a <c>default</c> to a schema that has been written already.</summary>
    private static string WithDefault(string schema, string? literal) =>
        literal == null ? schema : Append(schema, "\"default\":" + literal);

    /// <summary>Adds a keyword to a schema that has been written already.</summary>
    /// <remarks>
    /// A <c>$ref</c> takes no siblings in OpenAPI 3.0 - they are ignored - so a reference is
    /// wrapped in <c>allOf</c>, which every tool reads. Anything else takes the key directly.
    /// </remarks>
    private static string Append(string schema, string keyword) {
        var suffix = "," + keyword + "}";

        return schema.StartsWith("{\"$ref\"", System.StringComparison.Ordinal)
            ? "{\"allOf\":[" + schema + "]" + suffix
            : schema.Substring(0, schema.Length - 1) + suffix;
    }

    private static string ObjectRef(
        INamedTypeSymbol named, Dictionary<string, string> components, HashSet<string> inProgress,
        Dictionary<string, EnumVocabulary> enums, IAssemblySymbol? compilationAssembly) {
        var name = SchemaName(named);
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

            var wireName = WireName(property);
            var (hasDefault, defaultLiteral) = DefaultOf(named, property, compilationAssembly);

            properties
                .Append('"').Append(Escape(wireName)).Append("\":")
                .Append(WithDefault(
                    Describe(
                        Nullable(
                            SchemaConstraintWriter.Apply(
                                SchemaFor(property.Type, components, inProgress, enums, compilationAssembly),
                                property),
                            property.Type),
                        DocumentationOf(property)),
                    defaultLiteral));

            // A member that is always present belongs in required: a non-nullable reference type
            // because the author said so, a non-nullable value type because C# serialization
            // cannot omit one, and anything carrying [Required]. Value types were left out, so a
            // document described int members as optional in every response that always sends them.
            // A member with a constructor default is the exception whatever its type: the caller may
            // omit it, and the server fills it in.
            if (!hasDefault &&
                ((property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                  property.Type.IsReferenceType) ||
                 (property.Type.IsValueType && !IsNullableValueType(property.Type)) ||
                 SchemaConstraintWriter.IsRequired(property))) {
                required.Add(wireName);
            }

            first = false;
        }

        var schema = new StringBuilder("{\"type\":\"object\"");

        var summary = DocumentationOf(named);

        if (summary != null) {
            schema.Append(",\"description\":\"").Append(Escape(summary)).Append('"');
        }

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

    /// <summary>
    /// The name the serializer writes for a member: <c>[JsonPropertyName]</c> where the member
    /// carries one, otherwise the property name in camelCase.
    /// </summary>
    /// <remarks>
    /// The document camelCased the member name unconditionally, so a member renamed for the wire
    /// was published under a name the wire never carries, and a client generated from the document
    /// read nothing for it without anything failing. The attribute on a positional record
    /// parameter reaches the property it declares, which is where this reads it.
    /// </remarks>
    private static string WireName(IPropertySymbol property) {
        foreach (var attribute in property.GetAttributes()) {
            if (attribute.AttributeClass?.Name == "JsonPropertyNameAttribute" &&
                attribute.AttributeClass.ContainingNamespace?.ToDisplayString() ==
                "System.Text.Json.Serialization" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string name &&
                name.Length > 0) {
                return name;
            }
        }

        return CamelCase(property.Name);
    }

    /// <summary>
    /// The component a type is written as: its name, with a constructed type's arguments spelled
    /// into it.
    /// </summary>
    /// <remarks>
    /// <c>Paged&lt;Todo&gt;</c> and <c>Paged&lt;Courier&gt;</c> used to share one component named
    /// <c>Paged</c>, written from whichever was reached first, so the second operation was
    /// documented as returning the first one's items and nothing said so. <c>PagedOfTodo</c> and
    /// <c>PagedOfCourier</c> are two components, in the spelling client generators already produce
    /// for a constructed type.
    /// </remarks>
    private static string SchemaName(ITypeSymbol type) {
        if (type is IArrayTypeSymbol array) {
            return SchemaName(array.ElementType) + "Array";
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named) {
            if (named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T) {
                return SchemaName(named.TypeArguments[0]);
            }

            return named.Name + "Of" + string.Join("And", named.TypeArguments.Select(SchemaName));
        }

        return type.Name;
    }

    /// <summary>
    /// Whether a member's positional parameter declares a default, and that default as the JSON
    /// literal the document writes under <c>default</c>.
    /// </summary>
    /// <remarks>
    /// A member whose constructor parameter carries a default is one the caller may omit: the
    /// server fills it in and answers as if it had been sent. The document said required for every
    /// non-nullable member, so a strictly validating client was made to send what the server did
    /// not need. Constructor parameters are matched to the property by name the way a record
    /// declares them. A default the document cannot spell still makes the member optional, it just
    /// carries no <c>default</c>, and so does a null one, which the member's nullability already
    /// says.
    ///
    /// <para>
    /// A property initializer counts, and is read from syntax because it is not on the symbol.
    /// <c>public string RequestContext { get; set; } = "";</c> says the same thing a parameter's
    /// <c>= ""</c> says - this when nothing sends it - and publishing such a member as required told
    /// a client to send a value the server was filling in for them. It carries no <c>default</c>
    /// either: the initializer is an expression rather than a constant, and half of them
    /// (<c>= []</c>, <c>= new()</c>, <c>= DateTime.UtcNow</c>) have no JSON spelling at all.
    /// </para>
    /// </remarks>
    private static (bool Declared, string? Literal) DefaultOf(
        INamedTypeSymbol owner, IPropertySymbol property, IAssemblySymbol? compilationAssembly) {
        foreach (var constructor in owner.InstanceConstructors) {
            foreach (var parameter in constructor.Parameters) {
                if (parameter.HasExplicitDefaultValue && parameter.Name == property.Name) {
                    return (true, DefaultLiteral(parameter, compilationAssembly));
                }
            }
        }

        return (HasInitializer(property), null);
    }

    /// <summary>Whether the property is declared with an initializer.</summary>
    /// <remarks>
    /// From the declaring syntax, which is the only place it is: an initializer compiles into the
    /// constructor body, so neither the symbol nor reflection over the built assembly can see one.
    /// A property declared in more than one place cannot carry two initializers, so the first
    /// reference that is a property declaration answers for all of them.
    /// </remarks>
    private static bool HasInitializer(IPropertySymbol property) {
        foreach (var reference in property.DeclaringSyntaxReferences) {
            if (reference.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null }) {
                return true;
            }
        }

        return false;
    }

    private static string? DefaultLiteral(IParameterSymbol parameter, IAssemblySymbol? compilationAssembly) {
        var value = parameter.ExplicitDefaultValue;

        if (value == null) {
            return null;
        }

        var type = parameter.Type is INamedTypeSymbol { IsGenericType: true } nullable &&
                   nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            ? nullable.TypeArguments[0]
            : parameter.Type;

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) {
            return EnumMemberLiteral(enumType, value, compilationAssembly);
        }

        return value switch {
            string text => "\"" + Escape(text) + "\"",
            char character => "\"" + Escape(character.ToString()) + "\"",
            bool flag => flag ? "true" : "false",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>
    /// An enum default in the vocabulary the wire carries, resolved the way <see cref="EnumSchema"/>
    /// resolves the values beside it.
    /// </summary>
    private static string? EnumMemberLiteral(
        INamedTypeSymbol enumType, object value, IAssemblySymbol? compilationAssembly) {
        var naming = EnumWireNaming.IsOwned(enumType, compilationAssembly)
            ? EnumWireNaming.For(enumType, EnumWireNaming.AssemblyDefault(enumType))
            : "MemberName";

        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>()) {
            if (!field.HasConstantValue || !Equals(field.ConstantValue, value)) {
                continue;
            }

            foreach (var (member, wire) in EnumWireNaming.Members(enumType, naming)) {
                if (member == field.Name) {
                    return "\"" + Escape(wire) + "\"";
                }
            }
        }

        return null;
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
            // The format is written, so the document says decimal rather than leaving a reader to
            // guess from a bare "number" - which this framework reads back as double, so a
            // code-first decimal did not survive its own contract. NSwag reads the pair as decimal
            // too; openapi-generator ignores the format and defaults number to decimal in C#
            // anyway, so naming it costs that reader nothing.
            SpecialType.System_Decimal => "{\"type\":\"number\",\"format\":\"decimal\"}",
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

    /// <summary>
    /// A string as JSON string content: backslash, quote, and every control character.
    /// </summary>
    /// <remarks>
    /// The control characters are not decoration. RFC 8259 forbids anything below U+0020 raw in a
    /// string, and <c>System.Text.Json</c>, <c>jq</c> and the reference page's own parser all
    /// refuse a document carrying one - which a multi-line description delivers the moment a
    /// contract's prose has a second line. The fast path returns the original string, because
    /// almost every value written here is a name or a single sentence.
    /// </remarks>
    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>
    /// The schema with <c>"null"</c> added to its type when the member may be null.
    /// </summary>
    /// <remarks>
    /// The 2020-12 spelling - a type array - for the same reason SchemaConstraintWriter's bounds
    /// use it: the version is not known where schemas are built, and the default document is one
    /// this spelling is correct in. A <c>$ref</c> is left alone; the referenced schema describes
    /// the type, and nullability of the member would need <c>anyOf</c>, which no reader of this
    /// document needed yet. The service sent null for members the document typed non-nullable,
    /// which is the defect this closes.
    /// </remarks>
    private static string Nullable(string schema, ITypeSymbol type) {
        if (type.NullableAnnotation != NullableAnnotation.Annotated && !IsNullableValueType(type)) {
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

    internal static string Escape(string value) {
        var clean = true;

        foreach (var ch in value) {
            if (ch == '\\' || ch == '"' || ch < ' ') {
                clean = false;
                break;
            }
        }

        if (clean) {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value) {
            switch (ch) {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (ch < ' ') {
                        builder.Append("\\u").Append(((int)ch).ToString("x4"));
                    } else {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
