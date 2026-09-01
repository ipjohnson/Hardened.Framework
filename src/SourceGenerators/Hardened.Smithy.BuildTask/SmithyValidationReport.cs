using System.Text.RegularExpressions;

namespace Hardened.Smithy.BuildTask;

/// <summary>
/// Reads the validation report the Smithy CLI prints to standard error.
/// </summary>
/// <remarks>
/// <para>
/// The CLI names the <c>.smithy</c> file, line and column of every finding, and passing its output
/// through as one blob threw that away: MSBuild pinned the whole report to the first model at 0,0,
/// so a five-file model with one bad line pointed the author at the wrong file. Parsing the report
/// turns each finding back into a diagnostic MSBuild can attribute.
/// </para>
/// <para>
/// The format is one banner block per finding:
/// </para>
/// <code>
/// ──  ERROR  ────────────────────────────────────────── Target.UnresolvedShape
/// Shape: probe#Svc
/// File:  bad.smithy:4:1
///
/// 4| service Svc {
///  | ^
///
/// service shape has an `operation` relationship to an unresolved shape
/// `probe#MissingOp`
/// </code>
/// <para>
/// The severity label is one of Smithy's four (<c>NOTE</c>, <c>WARNING</c>, <c>DANGER</c>,
/// <c>ERROR</c>; the last two fail validation), the trailing word is the CLI's own event id, the
/// <c>Shape:</c> line is optional, and the source excerpt is dropped here because a diagnostic
/// carrying the file and line already points an editor at the source. This is not a stable contract
/// the CLI publishes - it is the observed output of the pinned version, which is exactly what the
/// pin is for - so anything that does not parse yields no findings and the caller falls back to
/// passing the text through whole.
/// </para>
/// </remarks>
internal static class SmithyValidationReport {

    internal readonly struct Finding {
        public Finding(
            string severity, string id, string? shape,
            string file, int line, int column, string message) {
            Severity = severity;
            Id = id;
            Shape = shape;
            File = file;
            Line = line;
            Column = column;
            Message = message;
        }

        public string Severity { get; }

        /// <summary>The CLI's event id - <c>Target.UnresolvedShape</c> and friends.</summary>
        public string Id { get; }

        public string? Shape { get; }

        /// <summary>The path as the CLI printed it, which is relative to its working directory.</summary>
        public string File { get; }

        public int Line { get; }

        public int Column { get; }

        public string Message { get; }

        /// <summary>Whether this finding is one of the two severities that fail validation.</summary>
        public bool FailedValidation =>
            Severity == "ERROR" || Severity == "DANGER";
    }

    /// <summary>
    /// <c>──  ERROR  ─────── Target.UnresolvedShape</c>: box-drawing dashes, the severity, dashes
    /// padding the banner to width, then the event id.
    /// </summary>
    private static readonly Regex Header = new(
        "^──\\s+(NOTE|WARNING|DANGER|ERROR)\\s+─+(?:\\s+(\\S+))?\\s*$",
        RegexOptions.Compiled);

    internal static IReadOnlyList<Finding> Parse(string standardError) {
        var findings = new List<Finding>();
        var lines = standardError.Split('\n');
        var index = 0;

        while (index < lines.Length) {
            var header = Header.Match(lines[index].TrimEnd('\r'));

            index++;

            if (!header.Success) {
                continue;
            }

            string? shape = null;
            var file = "";
            var line = 0;
            var column = 0;
            var message = new List<string>();

            for (; index < lines.Length; index++) {
                var current = lines[index].TrimEnd('\r');

                if (Header.IsMatch(current)) {
                    break;
                }

                if (current.StartsWith("Shape:", StringComparison.Ordinal)) {
                    shape = current.Substring("Shape:".Length).Trim();
                } else if (current.StartsWith("File:", StringComparison.Ordinal)) {
                    ParseLocation(
                        current.Substring("File:".Length).Trim(), out file, out line, out column);
                } else if (current.Trim().Length > 0 && !IsExcerpt(current) && !IsSummary(current)) {
                    message.Add(current.Trim());
                }
            }

            // Joined with spaces because the CLI wraps one sentence across lines at its banner
            // width; the breaks are layout, not content.
            findings.Add(new Finding(
                header.Groups[1].Value, header.Groups[2].Value, shape,
                file, line, column, string.Join(" ", message)));
        }

        return findings;
    }

    /// <summary>
    /// A line of the source excerpt: a line number and a pipe, or the pipe alone under it carrying
    /// the caret.
    /// </summary>
    private static bool IsExcerpt(string line) {
        var index = 0;

        while (index < line.Length && line[index] == ' ') {
            index++;
        }

        while (index < line.Length && char.IsDigit(line[index])) {
            index++;
        }

        return index < line.Length && line[index] == '|';
    }

    /// <summary>The count line after the last finding, whose content the findings already carry.</summary>
    private static bool IsSummary(string line) =>
        line.StartsWith("FAILURE: Validated ", StringComparison.Ordinal) ||
        line.StartsWith("SUCCESS: Validated ", StringComparison.Ordinal);

    /// <summary>
    /// <c>path:line:column</c>, taken from the right because a Windows path carries a colon of
    /// its own. A location that does not end in two numbers is kept whole as the file.
    /// </summary>
    private static void ParseLocation(string text, out string file, out int line, out int column) {
        file = text;
        line = 0;
        column = 0;

        var last = text.LastIndexOf(':');

        if (last <= 0) {
            return;
        }

        var second = text.LastIndexOf(':', last - 1);

        if (second <= 0) {
            return;
        }

        if (int.TryParse(text.Substring(second + 1, last - second - 1), out var parsedLine) &&
            int.TryParse(text.Substring(last + 1), out var parsedColumn)) {
            file = text.Substring(0, second);
            line = parsedLine;
            column = parsedColumn;
        }
    }
}
