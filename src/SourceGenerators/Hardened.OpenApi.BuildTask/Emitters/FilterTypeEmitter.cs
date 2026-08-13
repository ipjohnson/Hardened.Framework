using System.Text;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

/// <summary>
/// Emits a partial attribute class from an x-filter-types definition.
/// The developer provides the other partial with interface implementations.
/// </summary>
internal static class FilterTypeEmitter {
    public static string Emit(FilterTypeModel filterType, bool excludeFromCoverage = false) {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {filterType.Namespace}");
        sb.AppendLine("{");

        if (excludeFromCoverage) {
            sb.AppendLine("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        }

        sb.AppendLine("[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]");
        sb.AppendLine($"public partial class {filterType.ClassName} : System.Attribute");
        sb.AppendLine("{");

        foreach (var prop in filterType.Properties) {
            var propType = prop.EnumType ?? prop.CSharpType;
            var defaultLiteral = FormatDefault(prop);
            sb.Append($"    public {propType} {prop.Name} {{ get; set; }}");
            if (defaultLiteral != null) {
                sb.Append($" = {defaultLiteral};");
            } else {
                sb.Append(";");
            }
            sb.AppendLine();
        }

        sb.AppendLine("}");
        sb.AppendLine("}");

        return sb.ToString();
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
