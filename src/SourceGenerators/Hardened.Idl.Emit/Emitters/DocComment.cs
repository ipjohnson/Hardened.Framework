using System.Collections.Generic;
using System.Text;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Free-form specification prose, as something safe to put after a <c>///</c>.
/// </summary>
/// <remarks>
/// <para>
/// A description is XML content once it is inside a doc comment, so an unescaped <c>&lt;</c> in
/// prose about a range makes the whole comment malformed. That is the only transformation applied.
/// </para>
/// <para>
/// The line structure is kept. It used to be collapsed to a single line, because CSharpAuthor
/// wrote a comment as one <c>WriteIndentedLine</c> and an embedded newline produced a line with
/// neither the indent nor the marker. CSharpAuthor writes them line by line now, so a description
/// that arrives with paragraphs in it keeps them - which is most of what makes a long one readable.
/// </para>
/// </remarks>
internal static class DocComment {

    public static string? Format(string? description) {
        if (string.IsNullOrWhiteSpace(description)) {
            return null;
        }

        var lines = new List<string>();

        foreach (var line in description!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')) {
            lines.Add(Escape(line).TrimEnd());
        }

        // Leading and trailing blank lines carry nothing and would render as a bare "///" against
        // the summary tags. Blank lines between paragraphs are kept, because those are structure.
        var first = 0;
        var last = lines.Count - 1;

        while (first <= last && lines[first].Length == 0) {
            first++;
        }

        while (last >= first && lines[last].Length == 0) {
            last--;
        }

        if (first > last) {
            return null;
        }

        var builder = new StringBuilder();

        for (var i = first; i <= last; i++) {
            if (i > first) {
                builder.Append('\n');
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    private static string Escape(string line) {
        var builder = new StringBuilder(line.Length);

        foreach (var character in line) {
            switch (character) {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
