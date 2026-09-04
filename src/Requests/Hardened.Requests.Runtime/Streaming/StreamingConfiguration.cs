namespace Hardened.Requests.Runtime.Streaming;

/// <inheritdoc cref="IStreamingConfiguration"/>
public class StreamingConfiguration : IStreamingConfiguration {
    /// <summary>
    /// The WHATWG standard's "every 15 seconds or so".
    /// </summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    public TimeSpan HeartbeatInterval { get; set; } = DefaultHeartbeatInterval;
}
