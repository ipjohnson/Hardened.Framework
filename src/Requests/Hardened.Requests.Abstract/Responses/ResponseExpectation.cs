using System.Globalization;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// What an <see cref="IResponseExpectation{TSelf}"/> implementation reads a body and a header with.
/// </summary>
/// <remarks>
/// The messages are the point. Each of these fails for a reason a test author can act on without
/// opening the client's generated code, and having them in one place keeps that wording the same
/// across every response type and every client library reading them.
/// </remarks>
public static class ResponseExpectation {

    /// <summary>The body, as the type the response declares it to be.</summary>
    /// <exception cref="InvalidOperationException">
    /// The response carried no body, or carried one of another type.
    /// </exception>
    public static T Body<T>(object? body) => body switch {
        T typed => typed,
        null => throw new InvalidOperationException(
            $"The response declares a body of {typeof(T).Name} and carried none."),
        _ => throw new InvalidOperationException(
            $"The response declares a body of {typeof(T).Name} and carried " +
            $"{body.GetType().Name}. The client deserialised this status into a different model " +
            "than the one the expectation names."),
    };

    /// <summary>A header the status is required to carry.</summary>
    /// <exception cref="InvalidOperationException">The header was not present.</exception>
    public static string RequiredHeader(IReadOnlyDictionary<string, string> headers, string name) {
        ArgumentNullException.ThrowIfNull(headers);

        return OptionalHeader(headers, name) ?? throw new InvalidOperationException(
            $"The response declares a {name} header and carried none. Present: " +
            (headers.Count == 0 ? "nothing" : string.Join(", ", headers.Keys)) + ".");
    }

    /// <summary>A header the status may carry, or null.</summary>
    public static string? OptionalHeader(IReadOnlyDictionary<string, string> headers, string name) {
        ArgumentNullException.ThrowIfNull(headers);

        if (headers.TryGetValue(name, out var value)) {
            return value;
        }

        // The caller's dictionary need not be case-insensitive, and header names on the wire are
        // not. A client that reports "location" would otherwise read as one that sent no header.
        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value;
            }
        }

        return null;
    }

    /// <summary>The Retry-After delay, which the status is required to carry.</summary>
    /// <exception cref="InvalidOperationException">
    /// The header was not present, or was not a whole number of seconds.
    /// </exception>
    public static TimeSpan RequiredRetryAfter(IReadOnlyDictionary<string, string> headers) =>
        Seconds(RequiredHeader(headers, KnownHeaders.RetryAfter));

    /// <summary>The Retry-After delay if the response carried one, or null.</summary>
    /// <exception cref="InvalidOperationException">
    /// The header was present and was not a whole number of seconds.
    /// </exception>
    public static TimeSpan? OptionalRetryAfter(IReadOnlyDictionary<string, string> headers) =>
        OptionalHeader(headers, KnownHeaders.RetryAfter) is { } value ? Seconds(value) : null;

    // Seconds only, which is what RetryAfter writes. The HTTP-date form is legal and is not read
    // back here, because turning one into a TimeSpan needs a clock and would make two runs of the
    // same assertion disagree.
    private static TimeSpan Seconds(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : throw new InvalidOperationException(
                $"The {KnownHeaders.RetryAfter} header read \"{value}\", which is not a number of " +
                "seconds. Only the delta-seconds form is read back.");
}
