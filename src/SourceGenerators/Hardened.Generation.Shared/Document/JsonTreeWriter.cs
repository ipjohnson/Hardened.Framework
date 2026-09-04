using System.Globalization;
using System.Text;

namespace Hardened.Generation.Document;

/// <summary>
/// Writes a <see cref="JsonNode"/> tree as indented JSON.
/// </summary>
/// <remarks>
/// <para>
/// Two-space indentation, one member per line, empty containers as <c>{}</c> and <c>[]</c>, and a
/// newline at the end of the file. The output is a function of the tree alone, which is what lets a
/// second build compare the file it would write against the one on disk and leave it untouched.
/// </para>
/// <para>
/// Strings are escaped the way the generator escapes them - <c>\"</c>, <c>\\</c>, the short
/// escapes for the common control characters and <c>\uXXXX</c> for the rest - and non-ASCII text is
/// written as itself. A reviewer reading the file sees <c>café</c>, not <c>café</c>.
/// </para>
/// </remarks>
internal static class JsonTreeWriter {

    public static string WriteIndented(JsonNode node) {
        var builder = new StringBuilder();

        Write(builder, node, 0);

        builder.Append('\n');

        return builder.ToString();
    }

    private static void Write(StringBuilder builder, JsonNode node, int depth) {
        switch (node) {
            case JsonObject obj:
                WriteObject(builder, obj, depth);
                break;
            case JsonArray array:
                WriteArray(builder, array, depth);
                break;
            case JsonString text:
                WriteString(builder, text.Value);
                break;
            case JsonNumber number:
                builder.Append(number.Text);
                break;
            case JsonBoolean boolean:
                builder.Append(boolean.Value ? "true" : "false");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonObject obj, int depth) {
        if (obj.Members.Count == 0) {
            builder.Append("{}");

            return;
        }

        builder.Append('{');

        for (var index = 0; index < obj.Members.Count; index++) {
            builder.Append(index == 0 ? "\n" : ",\n");
            Indent(builder, depth + 1);
            WriteString(builder, obj.Members[index].Key);
            builder.Append(": ");
            Write(builder, obj.Members[index].Value, depth + 1);
        }

        builder.Append('\n');
        Indent(builder, depth);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonArray array, int depth) {
        if (array.Items.Count == 0) {
            builder.Append("[]");

            return;
        }

        builder.Append('[');

        for (var index = 0; index < array.Items.Count; index++) {
            builder.Append(index == 0 ? "\n" : ",\n");
            Indent(builder, depth + 1);
            Write(builder, array.Items[index], depth + 1);
        }

        builder.Append('\n');
        Indent(builder, depth);
        builder.Append(']');
    }

    private static void Indent(StringBuilder builder, int depth) {
        builder.Append(' ', depth * 2);
    }

    /// <summary>
    /// The JSON escaping of <paramref name="value"/>, quotes included. Shared with the YAML writer,
    /// whose double-quoted scalar takes the same escapes.
    /// </summary>
    public static void WriteString(StringBuilder builder, string value) {
        builder.Append('"');

        foreach (var ch in value) {
            switch (ch) {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (ch < ' ') {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
