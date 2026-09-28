namespace Hardened.Aws.Lambda.Runtime.Streaming;

/// <summary>
/// How a response leaves the function: as one payload when the handler returns, as a stream that
/// opens at the first body byte, or as either, chosen per invocation.
/// </summary>
/// <remarks>
/// <para>
/// The wire protocol is fixed by the front door, not chosen by the function. A function URL in
/// <c>RESPONSE_STREAM</c> invoke mode expects the HTTP prelude and the eight null bytes before the
/// body; a function URL in <c>BUFFERED</c> mode and an API Gateway HTTP API expect the payload
/// format 2.0 JSON. The event the function receives is the same document either way, so the
/// deployment has to say which. That is <see cref="LambdaResponseModeConfiguration.EnvironmentVariable"/>,
/// set beside the invoke mode the function URL was created with.
/// </para>
/// <para>
/// The Runtime API itself takes either answer from any invocation: a runtime posts one payload, or
/// opens a streamed response with <c>Lambda-Runtime-Function-Response-Mode: streaming</c>.
/// <see cref="Mixed"/> is for a front door that reads each answer as whichever of the two it was
/// sent as.
/// </para>
/// </remarks>
public enum LambdaResponseMode
{
    /// <summary>
    /// The body collects and the whole response goes back when the handler returns. The default,
    /// and the only mode an HTTP API or a <c>BUFFERED</c> function URL can serve.
    /// </summary>
    Buffered,

    /// <summary>
    /// Every response travels as a stream: a buffered operation is one write and a close, a
    /// streaming one is a write per item. Requires a function URL in <c>RESPONSE_STREAM</c> mode.
    /// </summary>
    Stream,

    /// <summary>
    /// An event stream or a newline-delimited stream travels as a Lambda response stream, and every
    /// other response as one payload when the handler returns. Decided per invocation, at the first
    /// body byte, from the response's content type.
    /// </summary>
    /// <remarks>
    /// Requires a front door that takes both kinds of answer from one function. Whether a function
    /// URL in <c>RESPONSE_STREAM</c> mode reads an answer sent as one payload has not been checked,
    /// so <see cref="Stream"/> is the mode for one.
    /// </remarks>
    Mixed,
}
