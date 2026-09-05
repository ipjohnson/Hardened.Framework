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

    /// <summary>
    /// The response type a call was expected to answer with, from what the client reported.
    /// </summary>
    /// <remarks>
    /// The whole of what a <c>Returns&lt;T&gt;()</c> helper does, so the two client testing
    /// libraries share it rather than each phrasing the same failure differently. Where they differ
    /// is only in how they reach the three arguments: Kiota reports a refusal by throwing a model
    /// carrying the status and the headers, Refit by returning an envelope holding all three.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Another status was answered, or the body was not the one the type declares.
    /// </exception>
    public static TExpected Match<TExpected>(
        int status, object? body, IReadOnlyDictionary<string, string> headers)
        where TExpected : IResponseExpectation<TExpected> {

        if (status != TExpected.StatusCode) {
            throw new InvalidOperationException(
                $"Expected {TExpected.StatusCode} ({Name(typeof(TExpected))}), the call was answered " +
                status + Carrying(body) + ".");
        }

        return TExpected.FromResponse(body, headers);
    }

    /// <summary>
    /// The status a call was expected to answer with, and nothing about its body.
    /// </summary>
    /// <remarks>
    /// For the response types that state something the wire does not carry back and so are not
    /// expectations - <see cref="NotFound"/> naming the resource, <see cref="Conflict"/> its detail
    /// line. Asserting the status against one of those still reads in the vocabulary of the
    /// contract rather than as a number.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Another status was answered.</exception>
    public static void MatchStatus<TStatus>(int status, object? body = null)
        where TStatus : IDeclaresStatus {

        if (status != TStatus.StatusCode) {
            throw new InvalidOperationException(
                $"Expected {TStatus.StatusCode} ({Name(typeof(TStatus))}), the call was answered " +
                status + Carrying(body) + ".");
        }
    }

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

    private static string Carrying(object? body) =>
        body == null ? " with no body" : " carrying a " + Name(body.GetType());

    /// <summary>
    /// A type as it is written in source, because <c>NotFound`1</c> in a failure message is a
    /// worse answer than the name the test author typed.
    /// </summary>
    /// <remarks>
    /// Public because a client testing library writes messages of its own about the same types, and
    /// one of them naming <c>NotFound&lt;Problem&gt;</c> while another names <c>NotFound`1</c>
    /// would read as two different things.
    /// </remarks>
    public static string Name(Type type) =>
        type.IsGenericType
            ? type.Name[..type.Name.IndexOf('`')] +
              "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">"
            : type.Name;

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
