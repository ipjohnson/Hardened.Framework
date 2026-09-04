using System;
using System.Text;

namespace Hardened.Generation.Document;

/// <summary>
/// Writes a <see cref="JsonNode"/> tree as YAML.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately conservative, because the reader is not ours. Block style with two-space
/// indentation and key order preserved; numbers, booleans and null written as they are in the
/// JSON; empty containers as <c>{}</c> and <c>[]</c>, which YAML reads as flow collections. A
/// scalar is written plain only when it matches a strict safe pattern, and is double-quoted with
/// the JSON escapes otherwise - YAML 1.2 takes those verbatim. That quotes every <c>$ref</c>, every
/// path key and every multi-line description, and leaves <c>type: string</c> and
/// <c>application/json</c> plain, which is how OpenAPI YAML usually reads anyway.
/// </para>
/// <para>
/// <b>The safe pattern.</b> Letters, digits, <c>_</c>, <c>.</c>, <c>/</c> and <c>-</c>, starting
/// with a letter, a digit or an underscore. That rules out every YAML indicator at the start of a
/// scalar and every character that could open a comment, a mapping or a flow collection inside
/// one. What survives the pattern is then refused if YAML would read it as something other than
/// a string: the boolean and null words of YAML 1.1 and 1.2, whichever case they are written in,
/// and anything that reads as a number - an integer, a decimal, an exponent, a hex or octal
/// literal, or the <c>.inf</c> and <c>.nan</c> spellings. A key such as <c>200</c> is therefore
/// quoted, which is exactly what a response status needs to be.
/// </para>
/// <para>
/// Block scalars for long descriptions are a later nicety. A quoted scalar is correct, and correct
/// is what this writer is for.
/// </para>
/// </remarks>
internal static class YamlTreeWriter {

    public static string Write(JsonNode node) {
        var builder = new StringBuilder();

        WriteValue(builder, node, 0, inline: true);

        return builder.ToString();
    }

    /// <summary>
    /// Writes <paramref name="node"/> at <paramref name="depth"/>. <paramref name="inline"/> is true
    /// when the caller has already written the key and a space, and a scalar or an empty container
    /// belongs on the same line; a non-empty container starts on the next line either way.
    /// </summary>
    private static void WriteValue(StringBuilder builder, JsonNode node, int depth, bool inline) {
        switch (node) {
            case JsonObject obj when obj.Members.Count == 0:
                builder.Append("{}\n");
                break;
            case JsonArray array when array.Items.Count == 0:
                builder.Append("[]\n");
                break;
            case JsonObject obj:
                if (inline) {
                    builder.Append('\n');
                }

                WriteObject(builder, obj, depth);
                break;
            case JsonArray array:
                if (inline) {
                    builder.Append('\n');
                }

                WriteArray(builder, array, depth);
                break;
            default:
                WriteScalar(builder, node);
                builder.Append('\n');
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonObject obj, int depth) {
        foreach (var member in obj.Members) {
            Indent(builder, depth);
            WriteKey(builder, member.Key);
            builder.Append(':');

            WriteMemberValue(builder, member.Value, depth);
        }
    }

    /// <summary>
    /// The value after a key. A non-empty object goes one level deeper on the following lines; a
    /// non-empty array's entries sit at the key's own depth, which is the common YAML layout.
    /// </summary>
    private static void WriteMemberValue(StringBuilder builder, JsonNode value, int depth) {
        switch (value) {
            case JsonObject obj when obj.Members.Count > 0:
                builder.Append('\n');
                WriteObject(builder, obj, depth + 1);
                break;
            case JsonArray array when array.Items.Count > 0:
                builder.Append('\n');
                WriteArray(builder, array, depth + 1);
                break;
            default:
                builder.Append(' ');
                WriteValue(builder, value, depth + 1, inline: true);
                break;
        }
    }

    private static void WriteArray(StringBuilder builder, JsonArray array, int depth) {
        foreach (var item in array.Items) {
            Indent(builder, depth);
            builder.Append('-');

            switch (item) {
                case JsonObject obj when obj.Members.Count > 0:
                    // The first member shares the dash's line, the rest align under it.
                    builder.Append(' ');
                    WriteKey(builder, obj.Members[0].Key);
                    builder.Append(':');
                    WriteMemberValue(builder, obj.Members[0].Value, depth + 1);

                    for (var index = 1; index < obj.Members.Count; index++) {
                        Indent(builder, depth + 1);
                        WriteKey(builder, obj.Members[index].Key);
                        builder.Append(':');
                        WriteMemberValue(builder, obj.Members[index].Value, depth + 1);
                    }

                    break;
                case JsonArray inner when inner.Items.Count > 0:
                    builder.Append('\n');
                    WriteArray(builder, inner, depth + 1);
                    break;
                default:
                    builder.Append(' ');
                    WriteValue(builder, item, depth + 1, inline: true);
                    break;
            }
        }
    }

    private static void WriteKey(StringBuilder builder, string key) {
        if (IsPlainSafe(key)) {
            builder.Append(key);
        }
        else {
            JsonTreeWriter.WriteString(builder, key);
        }
    }

    private static void WriteScalar(StringBuilder builder, JsonNode node) {
        switch (node) {
            case JsonString text:
                if (IsPlainSafe(text.Value)) {
                    builder.Append(text.Value);
                }
                else {
                    JsonTreeWriter.WriteString(builder, text.Value);
                }

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

    private static void Indent(StringBuilder builder, int depth) {
        builder.Append(' ', depth * 2);
    }

    /// <summary>
    /// Whether <paramref name="value"/> can be written without quotes and be read back as the same
    /// string.
    /// </summary>
    internal static bool IsPlainSafe(string value) {
        if (value.Length == 0) {
            return false;
        }

        var first = value[0];

        if (!(IsLetter(first) || IsDigit(first) || first == '_')) {
            return false;
        }

        foreach (var ch in value) {
            if (!(IsLetter(ch) || IsDigit(ch) || ch == '_' || ch == '.' || ch == '/' || ch == '-')) {
                return false;
            }
        }

        return !ReadsAsBooleanOrNull(value) && !ReadsAsNumber(value);
    }

    private static bool IsLetter(char ch) => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

    private static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

    /// <summary>
    /// The words a YAML 1.1 or 1.2 reader turns into a boolean or a null, in any casing. The 1.1
    /// list is the longer one, and a reader on that schema is still common.
    /// </summary>
    private static bool ReadsAsBooleanOrNull(string value) {
        switch (value.ToLowerInvariant()) {
            case "true":
            case "false":
            case "yes":
            case "no":
            case "y":
            case "n":
            case "on":
            case "off":
            case "null":
            case "~":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether a scalar built from the safe alphabet reads as a number: an integer, a decimal, an
    /// exponent form, a hex or octal literal, or one of the special floats.
    /// </summary>
    private static bool ReadsAsNumber(string value) {
        var lowered = value.ToLowerInvariant();

        if (lowered == ".inf" || lowered == "-.inf" || lowered == ".nan") {
            return true;
        }

        if (lowered.StartsWith("0x", StringComparison.Ordinal) || lowered.StartsWith("0o", StringComparison.Ordinal)) {
            return true;
        }

        var index = 0;

        if (index < value.Length && (value[index] == '-' || value[index] == '+')) {
            index++;
        }

        var digitsBefore = 0;

        while (index < value.Length && IsDigit(value[index])) {
            index++;
            digitsBefore++;
        }

        var digitsAfter = 0;

        if (index < value.Length && value[index] == '.') {
            index++;

            while (index < value.Length && IsDigit(value[index])) {
                index++;
                digitsAfter++;
            }
        }

        if (digitsBefore == 0 && digitsAfter == 0) {
            return false;
        }

        if (index < value.Length && (value[index] == 'e' || value[index] == 'E')) {
            index++;

            if (index < value.Length && (value[index] == '-' || value[index] == '+')) {
                index++;
            }

            var exponentDigits = 0;

            while (index < value.Length && IsDigit(value[index])) {
                index++;
                exponentDigits++;
            }

            if (exponentDigits == 0) {
                return false;
            }
        }

        return index == value.Length;
    }
}
