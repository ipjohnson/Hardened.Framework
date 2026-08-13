using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

internal static class EnumEmitter {
    public static string Emit(SchemaModel schema, string ns) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"namespace {ns}.Models");
        sb.AppendLine("{");

        var enumName = NamingHelper.ToPascalCase(schema.Name);

        sb.AppendLine("[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]");
        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");

        for (var i = 0; i < schema.EnumValues.Count; i++) {
            var value = NamingHelper.ToPascalCase(schema.EnumValues[i]);
            sb.Append($"    {value}");
            if (i < schema.EnumValues.Count - 1) {
                sb.AppendLine(",");
            } else {
                sb.AppendLine();
            }
        }

        sb.AppendLine("}");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
