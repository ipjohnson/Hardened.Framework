using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Envelopes;

/// <summary>
/// What every envelope does with headers: read one whatever the dictionary's comparer, and copy
/// a delivery's headers onto the trigger request it builds.
/// </summary>
public static class TriggerHeaders {
    /// <summary>
    /// A header by name, whichever comparer the dictionary was built with.
    /// </summary>
    /// <remarks>
    /// A transport's own collection compares without regard to case and answers the first lookup;
    /// a plain dictionary a test built may not, and the scan is what keeps the two reading alike.
    /// </remarks>
    public static string? Get(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var direct)) {
            return direct.Count == 0 ? null : direct.ToString();
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value.Count == 0 ? null : header.Value.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// The delivery's headers as the trigger request's own: a case-insensitive copy, so the
    /// request owns its collection rather than reading through to a feature the server recycles.
    /// </summary>
    public static Dictionary<string, StringValues> Copy(IDictionary<string, StringValues> headers) {
        var copy = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers) {
            copy[header.Key] = header.Value;
        }

        return copy;
    }

    /// <summary>A path's remainder under <paramref name="prefix"/>, or null when it is not under it.</summary>
    /// <remarks>
    /// <c>/_triggers/timer/nightly</c> under <c>/_triggers/timer/</c> is <c>nightly</c>; a trailing
    /// slash is dropped, and an empty remainder is returned empty rather than null so the caller
    /// can say what a prefix with nothing after it means.
    /// </remarks>
    public static string? Under(string path, string prefix) {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) {
            return null;
        }

        return path.Substring(prefix.Length).TrimEnd('/');
    }

    /// <summary>
    /// A prefix as the envelopes compare it: rooted, and ending in a slash.
    /// </summary>
    public static string Prefix(string prefix) {
        var normalised = prefix.Trim();

        if (!normalised.StartsWith('/')) {
            normalised = "/" + normalised;
        }

        if (!normalised.EndsWith('/')) {
            normalised += "/";
        }

        return normalised;
    }
}
