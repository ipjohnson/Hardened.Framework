using System.Text.Json;

namespace Hardened.Functions.Testing.Containers;

/// <summary>
/// One line an application printed behind <see cref="ObservationMarker.Prefix"/>, parsed.
/// </summary>
public sealed class Observation {
    private Observation(JsonElement fields, string raw) {
        Fields = fields;
        Raw = raw;
    }

    /// <summary>The JSON object the application wrote, as parsed.</summary>
    public JsonElement Fields { get; }

    /// <summary>The line as printed, for a failure message.</summary>
    public string Raw { get; }

    /// <summary>A string field, or null when absent or not a string.</summary>
    public string? Get(string name) =>
        Fields.ValueKind == JsonValueKind.Object &&
        Fields.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Whether the observation carries <paramref name="name"/> equal to <paramref name="value"/>.</summary>
    public bool Has(string name, string value) => Get(name) == value;

    /// <summary>
    /// Every observation in a container's output, in the order printed.
    /// </summary>
    /// <remarks>
    /// A line that carries the prefix and then fails to parse is kept with its fields undefined
    /// rather than dropped: a test asserting on a count would otherwise pass over a handler that
    /// printed something malformed, and the raw line is what says so.
    /// </remarks>
    public static IReadOnlyList<Observation> Parse(string output) {
        var observations = new List<Observation>();

        foreach (var line in output.Split('\n')) {
            var trimmed = line.TrimEnd('\r');
            var start = trimmed.IndexOf(ObservationMarker.Prefix, StringComparison.Ordinal);

            if (start < 0) {
                continue;
            }

            var json = trimmed.Substring(start + ObservationMarker.Prefix.Length).Trim();

            try {
                using var document = JsonDocument.Parse(json);

                observations.Add(new Observation(document.RootElement.Clone(), trimmed));
            }
            catch (JsonException) {
                observations.Add(new Observation(default, trimmed));
            }
        }

        return observations;
    }

    public override string ToString() => Raw;
}
