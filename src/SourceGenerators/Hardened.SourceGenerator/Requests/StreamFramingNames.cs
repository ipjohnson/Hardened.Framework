namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The framings a handler can name, and what each one means to the generator.
/// </summary>
/// <remarks>
/// Strings rather than an enum because the model they sit in is compared as a value for
/// incremental caching, and because the emitter needs the runtime type's name anyway. Kept
/// together so the media type a framing commits to and the type that writes it cannot drift - the
/// document says one and the pipeline sends the other, and nothing would catch that.
/// </remarks>
public static class StreamFramingNames {
    /// <summary><c>[ServerSentEvents]</c>.</summary>
    public const string ServerSentEvents = "sse";

    /// <summary>The default when a handler names nothing.</summary>
    public const string Ndjson = "ndjson";

    /// <summary>The runtime type that frames it, for the emitted filter call.</summary>
    public static string FramingTypeName(string? framing) =>
        framing == ServerSentEvents
            ? "Hardened.Requests.Runtime.Filters.SseFraming"
            : "Hardened.Requests.Runtime.Filters.NdjsonFraming";

    /// <summary>
    /// The media type it puts on the wire, which is also what the OpenAPI document has to declare.
    /// </summary>
    public static string ContentType(string? framing) =>
        framing == ServerSentEvents ? "text/event-stream" : "application/x-ndjson";
}
