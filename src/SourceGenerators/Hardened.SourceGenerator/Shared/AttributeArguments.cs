using System.Collections.Generic;
using System.Text;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// The positional arguments of an attribute, read back out of the source text
/// <see cref="AttributeModel.Arguments"/> holds.
/// </summary>
/// <remarks>
/// <para>
/// The model joins arguments with ", " and keeps each one as it was written, quotes included - so
/// reading them back is splitting on the commas that separate arguments and no others.
/// <c>Arguments.Split(',')</c> is not that: <c>[OpenApiInfo("Depot", "1.0", "Parcels, pallets and
/// freight")]</c> lost everything after "Parcels", and the document published a truncated
/// description with nothing said.
/// </para>
/// <para>
/// A comma inside a string, a collection expression or a nested call belongs to that argument, so
/// the walk tracks strings - ordinary, verbatim and character literals - and bracket depth. A
/// named argument written positionally, <c>description: "..."</c>, carries its name into the text
/// and <see cref="Text"/> takes it back off.
/// </para>
/// </remarks>
internal static class AttributeArguments {

    /// <summary>The arguments as written, in order.</summary>
    public static IReadOnlyList<string> Split(string arguments) {
        var parts = new List<string>();

        if (arguments.Length == 0) {
            return parts;
        }

        var start = 0;
        var depth = 0;

        for (var index = 0; index < arguments.Length; index++) {
            var character = arguments[index];

            switch (character) {
                case '"':
                case '\'':
                    index = SkipLiteral(arguments, index);
                    break;

                case '(':
                case '[':
                case '{':
                    depth++;
                    break;

                case ')':
                case ']':
                case '}':
                    depth--;
                    break;

                case ',' when depth == 0:
                    parts.Add(arguments.Substring(start, index - start));
                    start = index + 1;
                    break;
            }
        }

        parts.Add(arguments.Substring(start));

        return parts;
    }

    /// <summary>
    /// The string the argument at <paramref name="index"/> holds, or the empty string where the
    /// attribute has no such argument.
    /// </summary>
    /// <param name="name">
    /// The parameter's name, so an argument written as <c>name: value</c> is found wherever it
    /// sits. C# allows a named argument out of position, and the position is only meaningful for
    /// the ones written without a name - which is why the count skips them.
    /// </param>
    /// <remarks>
    /// Anything that is not a string literal comes back as its source text. Nothing here reads a
    /// non-string argument, and returning the text is a better answer than an empty one for
    /// whatever does next.
    /// </remarks>
    public static string Text(IReadOnlyList<string> parts, int index, string? name = null) {
        var position = 0;

        foreach (var part in parts) {
            var argument = part.Trim();
            var declared = Name(argument);

            if (declared != null) {
                if (declared == name) {
                    return Unquote(argument.Substring(declared.Length + 1).TrimStart());
                }

                continue;
            }

            if (position++ == index) {
                return Unquote(argument);
            }
        }

        return "";
    }

    /// <summary>
    /// The index after a string or character literal beginning at <paramref name="start"/>.
    /// </summary>
    private static int SkipLiteral(string text, int start) {
        var quote = text[start];
        var verbatim = quote == '"' && start > 0 && text[start - 1] == '@';

        for (var index = start + 1; index < text.Length; index++) {
            var character = text[index];

            if (!verbatim && character == '\\') {
                index++;

                continue;
            }

            if (character != quote) {
                continue;
            }

            // A doubled quote inside a verbatim string is one quote, not the end of it.
            if (verbatim && index + 1 < text.Length && text[index + 1] == quote) {
                index++;

                continue;
            }

            return index;
        }

        // Unterminated, which the compiler will have its own thing to say about. Consuming the
        // rest is what keeps this from splitting the remainder into arguments nobody wrote.
        return text.Length - 1;
    }

    /// <summary>
    /// The parameter <c>name: value</c> names, or null for an argument written positionally.
    /// </summary>
    private static string? Name(string argument) {
        for (var index = 0; index < argument.Length; index++) {
            var character = argument[index];

            if (character == ':') {
                return index == 0 ? null : argument.Substring(0, index);
            }

            if (!char.IsLetterOrDigit(character) && character != '_' && character != '@') {
                return null;
            }
        }

        return null;
    }

    private static string Unquote(string argument) {
        var verbatim = argument.StartsWith("@\"");
        var start = verbatim ? 2 : 1;

        if (argument.Length < start + 1 || argument[argument.Length - 1] != '"' ||
            (!verbatim && argument[0] != '"')) {
            return argument;
        }

        var body = argument.Substring(start, argument.Length - start - 1);

        if (verbatim) {
            return body.Replace("\"\"", "\"");
        }

        var builder = new StringBuilder(body.Length);

        for (var index = 0; index < body.Length; index++) {
            if (body[index] == '\\' && index + 1 < body.Length) {
                index++;

                builder.Append(body[index] switch {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '0' => '\0',
                    var escaped => escaped
                });

                continue;
            }

            builder.Append(body[index]);
        }

        return builder.ToString();
    }
}
