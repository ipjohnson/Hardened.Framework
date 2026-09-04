using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// The response body while <see cref="ConditionalRequestFilter"/> is in place: either the transport
/// or nothing, decided on the first write.
/// </summary>
/// <remarks>
/// <para>
/// The decision needs the validator and the status, and those are on the response only once
/// something is about to write - so it is taken inside the first <c>Write</c> or <c>Flush</c>,
/// which is still before any byte reaches the transport and so before the headers are final on
/// any host. A flush decides as well as a write: on Kestrel a flush starts the response, and a
/// status changed after that throws.
/// </para>
/// <para>
/// A 304 is the status, the content headers removed, and every byte after that dropped. What
/// stays is what RFC 9110 §15.4.5 says a 304 carries when a 200 would have: <c>ETag</c>,
/// <c>Cache-Control</c>, <c>Vary</c>, <c>Last-Modified</c> and the rest. <c>Content-Type</c>,
/// <c>Content-Length</c> and <c>Content-Encoding</c> describe content the response does not have -
/// the coding was written by the compressing body on its own first write, one stage inside this
/// one, a moment before this decided there was nothing to encode.
/// </para>
/// <para>
/// <see cref="Position"/> is the count of bytes accepted, whichever way they went, which is what
/// the testing response and the API Gateway host read to decide whether a response has started.
/// </para>
/// </remarks>
internal sealed class ConditionalResponseStream : Stream {
    private readonly IExecutionResponse _response;
    private readonly Stream _transport;
    private readonly StringValues _ifNoneMatch;
    private readonly StringValues _ifModifiedSince;
    private Stream? _target;
    private long _accepted;

    public ConditionalResponseStream(
        IExecutionResponse response,
        Stream transport,
        StringValues ifNoneMatch,
        StringValues ifModifiedSince) {
        _response = response;
        _transport = transport;
        _ifNoneMatch = ifNoneMatch;
        _ifModifiedSince = ifModifiedSince;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _accepted;

    public override long Position {
        get => _accepted;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Decides, if nothing has yet. For a response that wrote no body at all - a HEAD answered by
    /// a handler that returns nothing, an empty 200 - the first write never comes, and the filter
    /// calls this as the chain returns.
    /// </summary>
    public void Complete() => Target();

    public override void Flush() => Target().Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        Target().FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) {
        Target().Write(buffer, offset, count);

        _accepted += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        Target().Write(buffer);

        _accepted += buffer.Length;
    }

    public override void WriteByte(byte value) {
        Target().WriteByte(value);

        _accepted++;
    }

    public override async Task WriteAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        await Target().WriteAsync(buffer.AsMemory(offset, count), cancellationToken);

        _accepted += count;
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
        await Target().WriteAsync(buffer, cancellationToken);

        _accepted += buffer.Length;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Where the bytes go, chosen once.
    /// </summary>
    private Stream Target() {
        if (_target != null) {
            return _target;
        }

        if (!NotModified()) {
            return _target = _transport;
        }

        var headers = _response.Headers;

        _response.Status = 304;

        headers.Remove(KnownHeaders.ContentType);
        headers.Remove(KnownHeaders.ContentLength);
        headers.Remove(KnownHeaders.ContentEncoding);

        return _target = Stream.Null;
    }

    /// <summary>
    /// Whether the caller holds what this response is about to send.
    /// </summary>
    /// <remarks>
    /// Only a 200 that nothing refused. A refusal recorded ahead of serialization travels on with
    /// the request and is written behind this stage with its own status, but it is checked here
    /// as well as through the status: a 304 in place of a refusal would tell a caller who may not
    /// read the resource that it has not changed.
    /// </remarks>
    private bool NotModified() {
        var response = _response;

        if (response.ExceptionValue != null || (response.Status ?? 200) != 200) {
            return false;
        }

        var headers = response.Headers;

        var etag = headers.TryGetValue(KnownHeaders.ETag, out var tag) ? tag.ToString() : null;

        DateTimeOffset? lastModified =
            headers.TryGetValue(KnownHeaders.LastModified, out var modified) &&
            HttpDate.TryParse(modified, out var when)
                ? when
                : null;

        return ConditionalGet.NotModified(_ifNoneMatch, _ifModifiedSince, etag, lastModified);
    }
}
