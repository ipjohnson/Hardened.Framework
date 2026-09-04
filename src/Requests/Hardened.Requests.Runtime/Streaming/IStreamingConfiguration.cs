namespace Hardened.Requests.Runtime.Streaming;

/// <summary>
/// How a streamed response is kept alive while the handler is quiet.
/// </summary>
/// <remarks>
/// Registered by the request module with its defaults, so every application has a heartbeat
/// without asking for one. Amend it with <c>services.ConfigureStreaming</c>.
/// </remarks>
public interface IStreamingConfiguration {
    /// <summary>
    /// How long a stream may be silent before a heartbeat is written, or <see cref="TimeSpan.Zero"/>
    /// for never.
    /// </summary>
    /// <remarks>
    /// Fifteen seconds by default: what the WHATWG standard suggests, and half of the tightest idle
    /// cut on the list - CloudFront drops a response that is quiet for 30 seconds between packets,
    /// and retries the request while the first invocation keeps streaming to nobody. Only a framing
    /// with something to write honours it; newline-delimited JSON has no comment syntax and stays
    /// silent whatever this says.
    /// </remarks>
    TimeSpan HeartbeatInterval { get; }
}
