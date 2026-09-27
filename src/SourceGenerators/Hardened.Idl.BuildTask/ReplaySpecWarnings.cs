using System.Globalization;
using Microsoft.Build.Framework;

namespace Hardened.Idl.BuildTask;

/// <summary>
/// Reports again the warnings a build task kept in a file, for a build that skipped that task.
/// </summary>
/// <remarks>
/// MSBuild skips a target whose outputs are newer than its inputs, and a skipped task reports
/// nothing. A warning about a contract was therefore reported by the build that ran the task and by
/// no build after it while the contract stayed unchanged, so an incremental build answered 0
/// warnings for a contract that still had the problem. The targets run this only when they skipped
/// the task that wrote the file.
/// </remarks>
public sealed class ReplaySpecWarnings : Microsoft.Build.Utilities.Task
{
    /// <summary>The file the task wrote, which is absent where it had nothing to report.</summary>
    [Required]
    public string WarningsFile { get; set; } = "";

    public override bool Execute()
    {
        foreach (var warning in SpecWarnings.Read(WarningsFile))
        {
            Log.LogWarning(
                null,
                warning.Code,
                null,
                warning.File,
                warning.Line,
                warning.Column,
                0,
                0,
                "{0}",
                warning.Message
            );
        }

        return true;
    }
}

/// <summary>
/// One reported warning, as <see cref="ReplaySpecWarnings"/> reads it back.
/// </summary>
internal readonly struct SpecWarning
{
    public SpecWarning(string code, string? file, string message, int line = 0, int column = 0)
    {
        Code = code;
        File = file;
        Message = message;
        Line = line;
        Column = column;
    }

    public string Code { get; }

    public string? File { get; }

    public string Message { get; }

    public int Line { get; }

    public int Column { get; }
}

/// <summary>
/// The file a task keeps its warnings in: one per line, the code, the file, the line, the column
/// and the message, separated by tabs.
/// </summary>
internal static class SpecWarnings
{
    /// <summary>
    /// Writes <paramref name="warnings"/>, or deletes the file where there are none, so the targets
    /// can tell from its existence whether there is anything to replay.
    /// </summary>
    public static void Write(string path, IReadOnlyCollection<SpecWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }

            return;
        }

        var directory = System.IO.Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        var lines = new List<string>(warnings.Count);

        foreach (var warning in warnings)
        {
            lines.Add(
                string.Join(
                    "\t",
                    Escape(warning.Code),
                    Escape(warning.File ?? ""),
                    warning.Line.ToString(CultureInfo.InvariantCulture),
                    warning.Column.ToString(CultureInfo.InvariantCulture),
                    Escape(warning.Message)
                )
            );
        }

        System.IO.File.WriteAllLines(path, lines);
    }

    public static IEnumerable<SpecWarning> Read(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            yield break;
        }

        foreach (var line in System.IO.File.ReadAllLines(path))
        {
            var parts = line.Split('\t');

            if (parts.Length != 5)
            {
                continue;
            }

            var file = Unescape(parts[1]);

            yield return new SpecWarning(
                Unescape(parts[0]),
                file.Length == 0 ? null : file,
                Unescape(parts[4]),
                Number(parts[2]),
                Number(parts[3])
            );
        }
    }

    private static int Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Unescape(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            i++;

            builder.Append(
                value[i] switch
                {
                    't' => '\t',
                    'r' => '\r',
                    'n' => '\n',
                    _ => value[i],
                }
            );
        }

        return builder.ToString();
    }
}
