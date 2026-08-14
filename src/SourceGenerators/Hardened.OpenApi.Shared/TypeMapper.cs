using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator;

internal static class TypeMapper {
    public static string MapToCSharpType(string? type, string? format, string? refName = null) {
        if (refName != null) {
            return NamingHelper.ToPascalCase(GetRefName(refName));
        }

        return (type?.ToLowerInvariant(), format?.ToLowerInvariant()) switch {
            ("string", "date-time") => "DateTime",
            ("string", "date") => "DateOnly",
            ("string", "uuid") => "string",
            ("string", "byte") => "byte[]",
            ("string", "binary") => "byte[]",
            ("string", _) => "string",
            ("integer", "int64") => "long",
            ("integer", "uint32") => "uint",
            ("integer", _) => "int",
            ("number", "float") => "float",
            ("number", "double") => "double",
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

        return MapToCSharpType(property.Type, property.Format);
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

        return MapToCSharpType(parameter.Type, parameter.Format);
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
            "DateTime" => TypeDefinition.Get(typeof(DateTime)),
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
    public static bool IsNonNullableValueType(string csType) =>
        csType switch {
            "int" or "uint" or "long" or "float" or "double" or "bool" or "DateTime" or "DateOnly" => true,
            _ => false
        };

    public static string GetRefName(string refPath) {
        var lastSlash = refPath.LastIndexOf('/');
        return lastSlash >= 0 ? refPath.Substring(lastSlash + 1) : refPath;
    }
}
