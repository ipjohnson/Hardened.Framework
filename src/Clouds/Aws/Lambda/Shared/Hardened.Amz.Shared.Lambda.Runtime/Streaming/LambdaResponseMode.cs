namespace Hardened.Amz.Shared.Lambda.Runtime.Streaming;

/// <summary>
/// How a response leaves the function: as one payload when the handler returns, or as a stream
/// that opens at the first body byte.
/// </summary>
/// <remarks>
/// <para>
/// The wire protocol is fixed by the front door, not chosen by the function. A function URL in
/// <c>RESPONSE_STREAM</c> invoke mode expects the HTTP prelude and the eight null bytes before the
/// body; a function URL in <c>BUFFERED</c> mode and an API Gateway HTTP API expect the payload
/// format 2.0 JSON. Neither accepts the other, and the event the function receives is the same
/// document either way, so the deployment has to say which. That is
/// <see cref="LambdaResponseModeConfiguration.EnvironmentVariable"/>, which
/// <c>Hardened.Amz.Cdk</c> writes beside the invoke mode it sets.
/// </para>
/// </remarks>
public enum LambdaResponseMode {
    /// <summary>
    /// The body collects and the whole response goes back when the handler returns. The default,
    /// and the only mode an HTTP API or a <c>BUFFERED</c> function URL can serve.
    /// </summary>
    Buffered,

    /// <summary>
    /// Every response travels as a stream: a buffered operation is one write and a close, a
    /// streaming one is a write per item. Requires a function URL in <c>RESPONSE_STREAM</c> mode.
    /// </summary>
    Stream
}
