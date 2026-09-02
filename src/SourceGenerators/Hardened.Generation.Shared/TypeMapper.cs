using CSharpAuthor;
using Hardened.Generation.Models;

namespace Hardened.Generation;

internal static class TypeMapper {
    public static string MapToCSharpType(string? type, string? format, string? refName = null) {
        if (refName != null) {
            return NamingHelper.ToPascalCase(GetRefName(refName));
        }

        return (type?.ToLowerInvariant(), format?.ToLowerInvariant()) switch {
            // DateTimeOffset, not DateTime, because RFC 3339 date-time carries an offset and
            // DateTime cannot hold one. Reading "2020-06-05T22:47:53+00:00" into a DateTime
            // converts it to the host's local time and writes the host's offset back, so the same
            // response left a server in New York and a server in Berlin as different bytes -
            // which replaying Plaid's published examples is what caught.
            ("string", "date-time") => "DateTimeOffset",
            ("string", "date") => "DateOnly",
            ("string", "uuid") => "string",
            ("string", "byte") => "byte[]",
            // Money as an exact decimal, in the two spellings the ecosystem actually uses. Neither
            // is in the OpenAPI specification, which names int32, int64, float and double and
            // nothing else - format is an open-valued property, so both are legal and both are
            // conventions rather than standards.
            //
            // "string" + "number" is openapi-generator's: ModelUtils.isDecimalSchema tests exactly
            // that pair, and around thirty of its language generators map it to a decimal type.
            // Ahead of ("string", _) or it would go on being a string.
            ("string", "number") => "decimal",
            ("string", "binary") => "byte[]",
            ("string", _) => "string",
            ("integer", "int64") => "long",
            ("integer", "uint32") => "uint",
            ("integer", _) => "int",
            ("number", "float") => "float",
            ("number", "double") => "double",
            // "number" + "decimal" is NSwag's, measured against 14.1.0: it answers decimal for this
            // pair and double for a formatless number. openapi-generator warns "Unknown `format`
            // decimal" and discards it, so a document written for that tool means a decimal by
            // leaving the format off - which is the one reading this cannot take, because the same
            // absence is how every other document says double.
            ("number", "decimal") => "decimal",
            ("number", _) => "double",
            ("boolean", _) => "bool",
            _ => "JsonElement"
        };
    }

    public static string MapPropertyToCSharpType(PropertyModel property) {
        if (property.Ref != null) {
            return NamingHelper.ToPascalCase(GetRefName(property.Ref));
        }

        if (property.IsArray) {
            var itemType = MapToCSharpType(
                property.ArrayItemsType,
                property.ArrayItemsFormat,
                property.ArrayItemsRef);
            return $"List<{itemType}>";
        }

        if (property.IsDictionary) {
            var valueType = property.DictionaryValueRef != null
                ? NamingHelper.ToPascalCase(GetRefName(property.DictionaryValueRef))
                : MapToCSharpType(property.DictionaryValueType, null);
            return $"Dictionary<string, {valueType}>";
        }

        if (property.EnumValues is { Count: > 0 }) {
            return "string";
        }

        return WidenForBounds(
            MapToCSharpType(property.Type, property.Format), property.Minimum, property.Maximum);
    }

    /// <summary>
    /// Widens an integer whose declared bounds do not fit the type its format implies.
    /// </summary>
    /// <remarks>
    /// <c>type: integer</c> with no format maps to <c>int</c>, which is right until the document
    /// says otherwise. DigitalOcean declares <c>eq_range_index_dive_limit</c> as an integer with a
    /// maximum of 4294967295 - a value an <c>int</c> cannot hold, so the generated model would
    /// overflow on a payload the specification calls valid. The bound is part of the type.
    /// </remarks>
    private static string WidenForBounds(string csType, decimal? minimum, decimal? maximum) {
        if (csType != "int" && csType != "long") {
            return csType;
        }

        var low = minimum ?? 0m;
        var high = maximum ?? 0m;

        // int to long only. A bound beyond long would need decimal, and decimal is not a type the
        // JSON type-info emitter carries nullability for - so a document declaring one keeps the
        // type its format implies, and the bound stays on the attribute rather than in the type.
        if (csType == "int" && (low < int.MinValue || high > int.MaxValue) &&
            low >= long.MinValue && high <= long.MaxValue) {
            return "long";
        }

        return csType;
    }

    public static string MapParameterToCSharpType(ParameterModel parameter) {
        if (parameter.Ref != null) {
            return NamingHelper.ToPascalCase(GetRefName(parameter.Ref));
        }

        if (parameter.IsArray) {
            var itemType = MapToCSharpType(
                parameter.ArrayItemsType,
                null,
                parameter.ArrayItemsRef);
            return $"List<{itemType}>";
        }

        return WidenForBounds(
            MapToCSharpType(parameter.Type, parameter.Format), parameter.Minimum, parameter.Maximum);
    }

    public static ITypeDefinition GetTypeDefinition(string ns, string csType, bool nullable) {
        var typeDef = GetPrimitiveTypeDefinition(csType);
        if (typeDef != null) {
            return nullable ? typeDef.MakeNullable() : typeDef;
        }

        if (csType.StartsWith("List<")) {
            var inner = csType.Substring(5, csType.Length - 6);
            var innerType = GetTypeDefinition(ns, inner, false);
            var listType = new GenericTypeDefinition(typeof(List<>), new[] { innerType });
            return nullable ? listType.MakeNullable() : listType;
        }

        if (csType.StartsWith("Dictionary<string, ")) {
            var inner = csType.Substring(19, csType.Length - 20);
            var innerType = GetTypeDefinition(ns, inner, false);
            var dictType = new GenericTypeDefinition(typeof(Dictionary<,>),
                new[] { TypeDefinition.Get(typeof(string)), innerType });
            return nullable ? dictType.MakeNullable() : dictType;
        }

        if (csType == "byte[]") {
            var arrayType = TypeDefinition.Get("System", "Byte[]");
            return nullable ? arrayType.MakeNullable() : arrayType;
        }

        var def = TypeDefinition.Get(ns, csType);
        return nullable ? def.MakeNullable() : def;
    }

    private static ITypeDefinition? GetPrimitiveTypeDefinition(string csType) {
        return csType switch {
            "string" => TypeDefinition.Get(typeof(string)),
            "int" => TypeDefinition.Get(typeof(int)),
            "uint" => TypeDefinition.Get(typeof(uint)),
            "long" => TypeDefinition.Get(typeof(long)),
            "float" => TypeDefinition.Get(typeof(float)),
            "double" => TypeDefinition.Get(typeof(double)),
            "bool" => TypeDefinition.Get(typeof(bool)),
            "decimal" => TypeDefinition.Get(typeof(decimal)),
            "short" => TypeDefinition.Get(typeof(short)),
            "ushort" => TypeDefinition.Get(typeof(ushort)),
            "ulong" => TypeDefinition.Get(typeof(ulong)),
            "byte" => TypeDefinition.Get(typeof(byte)),
            "sbyte" => TypeDefinition.Get(typeof(sbyte)),
            "DateTime" => TypeDefinition.Get(typeof(DateTime)),
            "DateTimeOffset" => TypeDefinition.Get(typeof(DateTimeOffset)),
            "DateOnly" => TypeDefinition.Get("System", "DateOnly"),
            "JsonElement" => TypeDefinition.Get("System.Text.Json", "JsonElement"),
            _ => null
        };
    }

    /// <summary>
    /// Whether a required property of this type is non-nullable by virtue of being a value type.
    /// </summary>
    /// <remarks>
    /// Emitting <c>[Required]</c> on one is what ValidationModules reports as VM0004 - a rule that
    /// can never fail, because an <c>int</c> parameter cannot be absent. The warning is correct and
    /// the attribute is noise, so it is not emitted. It matters beyond tidiness: this repository
    /// escalates warnings to errors under ContinuousIntegrationBuild, so a spec with a required
    /// integer property would fail CI while building clean locally.
    /// </remarks>
    /// <remarks>
    /// <c>JsonElement</c> belongs here for the same reason the numerics do, and its absence was
    /// worth a build of its own: it is a struct, so a required property that fell back to it drew
    /// <c>value.X is null</c> against a non-nullable value type. Generated enums are the remaining
    /// case and cannot be recognised from a type name - callers holding the spec pass those in
    /// separately.
    /// </remarks>
    public static bool IsNonNullableValueType(string csType) =>
        csType switch {
            "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "byte" or "sbyte" or
            "float" or "double" or "decimal" or "bool" or "char" or
            "DateTime" or "DateOnly" or "TimeOnly" or "DateTimeOffset" or "TimeSpan" or
            "Guid" or "JsonElement" => true,
            _ => false
        };

    /// <summary>
    /// As above, but able to recognise the enums this specification generates.
    /// </summary>
    /// <remarks>
    /// A generated enum is a value type and cannot be told apart from a record by its name alone,
    /// so callers holding the schema list pass it. Without this, a required enum-typed member draws
    /// a <c>[Required]</c>, and the validator answers it with <c>is null</c> against a value type -
    /// the same CS0037 the numerics used to produce, and what OpenAI's document hits 150 times.
    /// </remarks>
    public static bool IsNonNullableValueType(
        string csType, IEnumerable<SchemaModel>? schemas) =>
        IsNonNullableValueType(csType) ||
        IsGeneratedEnum(csType, schemas) ||
        IsGeneratedChoice(csType, schemas);

    /// <summary>
    /// Whether the type names a <c>oneOf</c> wrapper this specification generates.
    /// </summary>
    /// <remarks>
    /// A struct, for the same reason a generated enum is one: an optional payload is then <c>T?</c>
    /// and a required one cannot be null, which is what the schema says either way. Every caller
    /// that had to know an enum was a value type has to know this too, and there is exactly one
    /// place that answers the question.
    /// </remarks>
    public static bool IsGeneratedChoice(string csType, IEnumerable<SchemaModel>? schemas) {
        if (schemas == null) {
            return false;
        }

        foreach (var schema in schemas) {
            if (schema.Kind == SchemaKind.OneOf && NamingHelper.ToPascalCase(schema.Name) == csType) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type names an enum this specification generates.
    /// </summary>
    /// <remarks>
    /// Such a member is already restricted to the enum's own members, so <c>[AllowedValues]</c>
    /// adds nothing - and, because it renders its arguments as string literals, it compares the
    /// enum against strings and does not compile.
    /// </remarks>
    public static bool IsGeneratedEnum(string csType, IEnumerable<SchemaModel>? schemas) {
        if (schemas == null) {
            return false;
        }

        foreach (var schema in schemas) {
            if (schema.Kind == SchemaKind.Enum && NamingHelper.ToPascalCase(schema.Name) == csType) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type has a <c>Count</c> for <c>[ItemCount]</c> to check.
    /// </summary>
    /// <remarks>
    /// Both <c>minItems</c> on an array and <c>minProperties</c> on a dictionary become
    /// <c>[ItemCount]</c>, and both of those types have a count. A schema carrying those bounds
    /// that did not map to either - an array whose element type could not be named, say - lands on
    /// <c>JsonElement</c>, and the emitted validator then reads <c>.Count</c> off a struct with no
    /// such member.
    /// </remarks>
    public static bool HasItemCount(string csType) =>
        csType.StartsWith("List<", System.StringComparison.Ordinal) ||
        csType.StartsWith("Dictionary<", System.StringComparison.Ordinal) ||
        csType.EndsWith("[]", System.StringComparison.Ordinal);

    /// <summary>Whether numeric bounds can be compared against this type.</summary>
    public static bool IsNumeric(string csType) =>
        csType switch {
            "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "byte" or "sbyte" or
            "float" or "double" or "decimal" => true,
            _ => false
        };

    /// <summary>Whether a length can be read off this type.</summary>
    public static bool IsStringLike(string csType) =>
        csType == "string";

    /// <summary>
    /// A C# type name, fully qualified.
    /// </summary>
    /// <remarks>
    /// For emitters that build source as text and so never pass through the output context that
    /// qualifies everything else. Zoom declares a schema named <c>DateTime</c>, and the generated
    /// file imports the models namespace - so an unqualified <c>typeof(DateTime?)</c> bound to the
    /// record rather than to <c>System.DateTime</c>, and a nullable reference type is not something
    /// <c>typeof</c> accepts (CS8639). Generated code should not depend on what happens to be in
    /// scope.
    /// </remarks>
    public static string QualifiedName(string ns, string csType, bool nullable) {
        var builder = new System.Text.StringBuilder();

        GetTypeDefinition(ns, csType, nullable).WriteTypeName(builder, TypeOutputMode.Global);

        return builder.ToString();
    }

    /// <summary>
    /// The name a reference names.
    /// </summary>
    /// <remarks>
    /// Tolerant of the shape the reference arrived in, because a front-end may not have normalized
    /// it - it takes the last segment, so <c>#/components/schemas/Pet</c> and a bare <c>Pet</c> both
    /// answer <c>Pet</c>. Paired with <see cref="MakeRef"/>: read with this, write with that, and no
    /// caller has to know the form.
    /// </remarks>
    public static string GetRefName(string refPath) {
        var lastSlash = refPath.LastIndexOf('/');
        return lastSlash >= 0 ? refPath.Substring(lastSlash + 1) : refPath;
    }

    /// <summary>
    /// A reference to a schema by name, in the form the model holds.
    /// </summary>
    /// <remarks>
    /// The model is an internal representation, so the form is the spine's to define rather than
    /// any one description language's. It happens to be the OpenAPI pointer, because that is the
    /// front-end there is - but the point is that the passes which rewrite references
    /// (<c>NameAllocator</c> renaming a type, <c>SpecSlicer</c> walking to a derived one) call this
    /// rather than concatenating the pointer themselves. Those passes never read a description;
    /// they are what a second front-end - Smithy, whose references are shape ids like
    /// <c>com.example#Pet</c> - would reuse unchanged, and a pointer spelled inline in them is a
    /// dependency on OpenAPI that nothing declares.
    /// </remarks>
    public static string MakeRef(string schemaName) => "#/components/schemas/" + schemaName;
}
