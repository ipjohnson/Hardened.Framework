using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Conditional;

/// <summary>
/// The response body while <see cref="ConditionalGetFilter"/> is in place: the transport, nothing,
/// or a buffer, decided on the first write.
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
/// A response that carries an <c>ETag</c> at that moment is judged at once: a 304, or the bytes
/// straight through. One that carries none is held back, because the tag has to cover all of the
/// bytes and the headers have to be final before the first of them goes out. The tag is computed
/// and the conditionals judged as the chain returns, in <see cref="CompleteAsync"/>.
/// </para>
/// <para>
/// A 304 is the status, the content headers removed, and every byte after that dropped. What
/// stays is what RFC 9110 §15.4.5 says a 304 carries when a 200 would have: <c>ETag</c>,
/// <c>Cache-Control</c>, <c>Vary</c>, <c>Last-Modified</c> and the rest. <c>Content-Type</c>,
/// <c>Content-Length</c> and <c>Content-Encoding</c> describe content the response does not have -
/// the coding was written by the compressing body on its own first write, one stage inside this
/// one, before this decided there was nothing to encode.
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
    private MemoryStream? _buffer;
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
    /// Finishes the response once the chain has returned: a held-back body is tagged, judged and
    /// written out, or dropped for a 304. A response that never wrote is an empty one and is
    /// treated the same way.
    /// </summary>
    /// <param name="decide">
    /// False when the chain threw. The bytes are still written, since the error path serialized
    /// into the same buffer, but nothing is tagged and no 304 is decided underneath a failure.
    /// </param>
    public async ValueTask CompleteAsync(bool decide, CancellationToken cancellationToken) {
        // Decided on the first write: the transport has the bytes, or nothing does.
        if (_target != null && _buffer == null) {
            return;
        }

        if (decide) {
            if (Storable() && !HasTag()) {
                _response.Headers[KnownHeaders.ETag] = EntityTagHeader.ForContent(Held());
            }

            if (NotModified()) {
                Discard();

                return;
            }
        }

        if (_buffer != null) {
            _buffer.Position = 0;

            await _buffer.CopyToAsync(_transport, cancellationToken);
        }
    }

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

        // No validator yet. The bytes are held back so one can be computed over all of them, and
        // the caller's conditionals wait for it. This is the cost of declaring [ConditionalGet] on
        // a handler that writes no tag of its own.
        if (!HasTag()) {
            return _target = _buffer = new MemoryStream();
        }

        return _target = NotModified() ? Discard() : _transport;
    }

    /// <summary>
    /// Turns the response into a 304 and answers with nowhere for the bytes to go.
    /// </summary>
    private Stream Discard() {
        var headers = _response.Headers;

        _response.Status = 304;

        headers.Remove(KnownHeaders.ContentType);
        headers.Remove(KnownHeaders.ContentLength);
        headers.Remove(KnownHeaders.ContentEncoding);

        return Stream.Null;
    }

    /// <summary>
    /// The bytes held back so far, or none.
    /// </summary>
    private ReadOnlySpan<byte> Held() =>
        _buffer == null ? ReadOnlySpan<byte>.Empty : _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length);

    private bool HasTag() => _response.Headers.ContainsKey(KnownHeaders.ETag);

    /// <summary>
    /// Whether this is a representation a caller could hold: a 200 that nothing refused.
    /// </summary>
    /// <remarks>
    /// A refusal recorded ahead of serialization travels on with the request and is written
    /// behind this stage with its own status, but it is checked here as well as through the
    /// status: a tag on a refusal, or a 304 in place of one, would tell a caller who may not read
    /// the resource what it holds.
    /// </remarks>
    private bool Storable() =>
        !_response.Refused && (_response.Status ?? 200) == 200;

    /// <summary>
    /// Whether the caller holds what this response is about to send. Asked only once the response
    /// carries a tag: found there on the first write, or put there as the chain returned.
    /// </summary>
    private bool NotModified() {
        if (!Storable()) {
            return false;
        }

        var headers = _response.Headers;

        // A Last-Modified nothing can read is a Last-Modified the response does not have.
        DateTimeOffset? lastModified =
            headers.TryGetValue(KnownHeaders.LastModified, out var modified) &&
            HttpDate.TryParse(modified, out var when)
                ? when
                : null;

        return Precondition.NotModified(
            _ifNoneMatch, _ifModifiedSince, headers[KnownHeaders.ETag].ToString(), lastModified);
    }
}
