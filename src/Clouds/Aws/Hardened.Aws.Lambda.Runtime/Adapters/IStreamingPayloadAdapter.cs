using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// An adapter whose source can take a streamed response, and which knows what to put in the
/// prelude that opens one.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPayloadAdapter"/> rather than a member on it, because most adapters
/// have no answer to give. A queue message, a stream record and a scheduled rule have no caller
/// holding a connection and no status to send one, so asking every adapter to build an HTTP prelude
/// would put a <c>NotSupportedException</c> in five implementations to serve one.
/// </para>
/// <para>
/// This is also what decides the mode per function rather than per deployment.
/// <c>HARDENED_LAMBDA_RESPONSE_MODE</c> is read once whatever the function serves, and a function
/// whose adapter does not implement this stays buffered under it - which is right, because the
/// variable describes the front door and an event source has none.
/// </para>
/// </remarks>
public interface IStreamingPayloadAdapter : IPayloadAdapter {
    /// <summary>
    /// The status, headers and cookies to open the response stream with.
    /// </summary>
    /// <remarks>
    /// Called at the first body byte rather than when the response is created, so what it reads is
    /// whatever the pipeline had decided by then. A refusal serialized before any handler ran opens
    /// the stream with the refusal's status, and a handler that set headers beside its first item
    /// has them here.
    /// </remarks>
    HttpResponseStreamPrelude CreatePrelude(IExecutionResponse response);
}
