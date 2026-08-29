using System.Collections.Generic;
using System.Text;

namespace Hardened.Idl.Emitters;

/// <summary>
/// Free-form specification prose, as something safe to put after a <c>///</c>.
/// </summary>
/// <remarks>
/// <para>
/// Escaping is not applied here, deliberately. CSharpAuthor 2.0 escapes the three XML markup
/// characters itself when it writes a comment, so a layer here would double-escape -
/// <c>&amp;rarr;</c> arrived in generated docs as <c>&amp;amp;rarr;</c>. Exactly one layer owns
/// escaping, and it is the one that owns the <c>///</c> markers.
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
            lines.Add(line.TrimEnd());
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
}
