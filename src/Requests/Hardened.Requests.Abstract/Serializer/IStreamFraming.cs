using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// What goes around each item of a streamed response, and what the stream is called on the wire.
/// </summary>
/// <remarks>
/// <para>
/// The serializer writes the item; this writes everything else. Splitting them is what makes
/// newline-delimited JSON and server-sent events one code path rather than two filters: the loop,
/// the cancellation, the per-item flush and the compression rule are identical between them, and
/// the difference is a prefix, a suffix and a content type.
/// </para>
/// <para>
/// <b>An implementation must not assume it is writing JSON.</b> It is handed the serializer the
/// pipeline resolved, which answers for the content type this framing committed to - so a framing
/// that wanted a different payload encoding registers a serializer for its media type rather than
/// encoding anything itself.
/// </para>
/// </remarks>
public interface IStreamFraming {
    /// <summary>
    /// What the response commits to before the first item is written.
    /// </summary>
    /// <remarks>
    /// Committed once for the whole stream rather than per item. The response serializers assign a
    /// content type on entry, which is right for a buffered response and wrong for one item of a
    /// stream, and for <c>text/event-stream</c> it is not a cosmetic difference - a browser
    /// <c>EventSource</c> refuses any other content type.
    /// </remarks>
    string ContentType { get; }

    /// <summary>
    /// Writes one item, calling <paramref name="serialize"/> for the payload.
    /// </summary>
    /// <param name="context">
    /// The item to write is already on <c>context.Response.ResponseValue</c>, because that is where
    /// <paramref name="serialize"/> reads it from.
    /// </param>
    /// <param name="serialize">
    /// The pipeline's serializer, resolved for <see cref="ContentType"/>. Called between whatever
    /// this framing writes before and after it.
    /// </param>
    ValueTask WriteItem(IExecutionContext context, Func<IExecutionContext, Task> serialize);

    /// <summary>
    /// Writes whatever ends the stream, after the last item and also after no items at all.
    /// </summary>
    /// <remarks>
    /// Always called, including for an empty stream, and that is the point: Lambda Function URLs do
    /// not close a zero-byte body promptly, so a reader waiting on one hangs. Every framing has to
    /// put at least one byte on the wire.
    /// </remarks>
    ValueTask WriteCompletion(IExecutionContext context);

    /// <summary>
    /// Writes something a client discards, to keep a quiet connection open, and says whether it
    /// did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the filter when the handler has produced nothing for the configured interval. An
    /// intermediary that cuts an idle response - CloudFront after 30 seconds between packets, a
    /// default nginx after 60, Azure's load balancer after 230 - sees bytes instead, and the client
    /// is spared a reconnect, a replay from <c>Last-Event-ID</c> and, on Lambda, a second
    /// invocation.
    /// </para>
    /// <para>
    /// <c>false</c> means the format has no way to say nothing: newline-delimited JSON has no
    /// comment syntax. The filter stops asking after the first <c>false</c>. The default answers
    /// that, so a framing written before this member existed keeps compiling and keeps its
    /// behaviour.
    /// </para>
    /// </remarks>
    ValueTask<bool> WriteHeartbeat(IExecutionContext context) => new(false);
}
