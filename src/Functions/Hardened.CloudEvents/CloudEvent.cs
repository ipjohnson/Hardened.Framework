namespace Hardened.CloudEvents;

/// <summary>
/// One event, whichever form it arrived in.
/// </summary>
/// <remarks>
/// <para>
/// The four required context attributes of a CloudEvents 1.0 event are the constructor, so an
/// instance cannot exist without them; everything else is optional and is null when the producer
/// sent nothing. The event's payload is <see cref="Data"/>, as bytes, because that is what a
/// handler's binder reads and because the two forms carry it differently: structured JSON carries
/// it as a JSON value or a base64 string, and the binary form carries it as the whole body.
/// </para>
/// <para>
/// <see cref="Time"/> is kept as the producer wrote it rather than parsed. An adapter puts it in a
/// header, where the text is what a handler reads, and a timestamp the producer wrote outside
/// RFC 3339 is better carried than refused.
/// </para>
/// </remarks>
public sealed record CloudEvent(string SpecVersion, string Id, string Source, string Type) {

    /// <summary>The subject of the event in the context of the producer, or null.</summary>
    public string? Subject { get; init; }

    /// <summary>When the occurrence happened, as the producer wrote it, or null.</summary>
    public string? Time { get; init; }

    /// <summary>The content type of <see cref="Data"/>, or null when the producer said nothing.</summary>
    public string? DataContentType { get; init; }

    /// <summary>The schema <see cref="Data"/> adheres to, or null.</summary>
    public string? DataSchema { get; init; }

    /// <summary>The payload, as bytes. Empty when the event carried none.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// Every extension attribute the event carried, keyed by its lower-case name without the
    /// <c>ce-</c> prefix the binary form gives it.
    /// </summary>
    /// <remarks>
    /// Values are strings whatever the JSON carried, because the binary form can only carry
    /// strings and the two forms have to read alike. A number or a boolean in the structured form
    /// arrives as its JSON text.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Extensions { get; init; } = NoExtensions;

    private static readonly IReadOnlyDictionary<string, string> NoExtensions =
        new Dictionary<string, string>();
}
