using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// What a generated binder does with a body parameter declared as <c>byte[]</c> or
/// <c>Stream</c>: hands over the bytes, with nothing between them and the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>No deserializer, and no <c>Content-Type</c>.</b> These two types are the payload rather than
/// a shape to read out of one, so there is nothing to parse and nothing for a media type to
/// select. The binder emitted <c>DeserializeRequestBody&lt;byte[]&gt;</c> for every body parameter
/// whatever its type, which reached the JSON reader - the default deserializer, and the one that
/// reads every body no other deserializer claimed - and refused a blob with
/// <c>'0x7F' is an invalid start of a value</c> under every <c>Content-Type</c>, including none.
/// A code-first blob upload could not be written: the parameter bound, the document published a
/// <c>requestBody</c>, and no request could satisfy it.
/// </para>
/// <para>
/// This is the request-side half of a rule the rest of the framework already applied. A handler
/// <em>returning</em> <c>byte[]</c> or <c>Stream</c> has its response written by
/// <c>RawResponseSerializer</c> with no serializer consulted, and the schema writer states it
/// outright: "byte[] is the payload itself, not a sequence of numbers."
/// </para>
/// <para>
/// A body of base64 inside a JSON document is what the old reading happened to accept, and it is
/// not what the published document described - a <c>byte[]</c> body publishes
/// <c>{"type":"string","format":"binary"}</c>, so no client generated from it ever sent one. That
/// shape stays available on a member of a model, where the JSON reader still reads it.
/// </para>
/// </remarks>
public static class RawBody
{
    /// <summary>
    /// The whole body, or null where there was none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Zero bytes is null, not an empty array.</b> A handler declaring the parameter
    /// non-nullable then refuses a bodyless request the way every other missing body is refused, a
    /// 400 naming the parameter through <c>RequestBody.Required</c>, and that is the only reading
    /// under which <c>required</c> means anything for a blob: a transport delivers a request that
    /// carried no body and one that carried <c>Content-Length: 0</c> as the same empty stream, so a
    /// rule that told them apart would be a rule about the host rather than about the caller. A
    /// handler that wants the empty upload declares <c>byte[]?</c> and gets the null.
    /// </para>
    /// <para>
    /// Copied rather than handed over, because the stream belongs to the transport and does not
    /// outlive the request. <c>ByPayload</c> reads the same stream the same way and rewinds it, so
    /// a handler behind a payload-keyed cache still sees the bytes.
    /// </para>
    /// </remarks>
    public static async ValueTask<byte[]?> Bytes(IExecutionContext context)
    {
        var body = context.Request.Body;

        if (body == Stream.Null)
        {
            return null;
        }

        var buffer = new MemoryStream();

        await body.CopyToAsync(buffer, context.CancellationToken);

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    /// <summary>
    /// The body itself, or null where the request carried none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transport's own stream, unread. A handler taking one has said it will do the reading,
    /// which is the point of asking for a <c>Stream</c> rather than a <c>byte[]</c>: an upload
    /// larger than the process wants to hold goes to its destination a chunk at a time. It is only
    /// readable while the request is, so a handler that stores it and reads it later reads nothing.
    /// </para>
    /// <para>
    /// <b>An empty body is a stream here, where it is null above.</b> Emptiness is not a property
    /// of a stream that can be read without consuming it, and consuming it is the one thing this
    /// must not do. So a <c>Stream</c> parameter declared non-nullable is refused only for a
    /// request that carried no body object at all, and a handler that cares whether any bytes
    /// arrived finds that out by reading.
    /// </para>
    /// </remarks>
    public static Stream? Body(IExecutionContext context)
    {
        var body = context.Request.Body;

        return body == Stream.Null ? null : body;
    }
}
