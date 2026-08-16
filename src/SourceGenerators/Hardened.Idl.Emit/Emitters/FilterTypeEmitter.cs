using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Emits a partial attribute class from an x-filter-types definition.
/// The developer provides the other partial with interface implementations.
/// </summary>
internal static class FilterTypeEmitter {

    public static ClassDefinition Emit(IConstructContainer container, FilterTypeModel filterType) {
        var attribute = container.AddClass(filterType.ClassName);

        attribute.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        attribute.AddBaseType(TypeDefinition.Get(typeof(System.Attribute)));

        attribute.AddAttribute(
            TypeDefinition.Get(typeof(System.AttributeUsageAttribute)),
            new CodeOutputComponent(
                "System.AttributeTargets.Class | System.AttributeTargets.Method") { Indented = false });

        foreach (var property in filterType.Properties) {
            var propertyType = property.EnumType ?? property.CSharpType;

            var definition = attribute.AddProperty(
                TypeMapper.GetTypeDefinition(filterType.Namespace, propertyType, false), property.Name);

            definition.Modifiers |= ComponentModifier.Public;

            var defaultLiteral = FormatDefault(property);

            if (defaultLiteral != null) {
                definition.DefaultValue = new CodeOutputComponent(defaultLiteral) { Indented = false };
            }
        }

        return attribute;
    }

    private static string? FormatDefault(FilterTypePropertyModel prop) {
        if (prop.Default == null) return null;

        if (prop.EnumType != null) {
            return $"{prop.EnumType}.{prop.Default}";
        }

        return prop.CSharpType switch {
            "string" => $"\"{EscapeString(prop.Default)}\"",
            "bool" => prop.Default.ToLowerInvariant(),
            "int" or "long" or "float" or "double" => prop.Default,
            _ => $"\"{EscapeString(prop.Default)}\""
        };
    }

    private static string EscapeString(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
