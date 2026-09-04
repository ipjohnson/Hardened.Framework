using System.IO.Compression;
using Hardened.Requests.Abstract.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Compression;

namespace Hardened.Web.Runtime.Compression;

/// <summary>
/// The response body while <see cref="ResponseCompressionFilter"/> is in place: identity bytes in,
/// and either the same bytes or a compressed member out, decided on the first write.
/// </summary>
/// <remarks>
/// <para>
/// <b>The decision waits for the first write because that is when it can be made.</b> Choosing a
/// coding at filter entry is negotiation; whether to compress at all depends on the content type
/// and the status, and those are set by whatever writes the body. So the encoder is opened, and
/// the headers written, inside the first <c>Write</c> - which is still before any byte reaches
/// the transport, so the headers are final before the response starts on every host.
/// </para>
/// <para>
/// A flush before any write flushes the transport and decides nothing. On Kestrel that starts the
/// response, and a write arriving afterwards sees <c>ResponseStarted</c> and passes through,
/// because the headers it would need to change have already been sent.
/// </para>
/// <para>
/// <see cref="Position"/> is the count of bytes accepted, which is what the API Gateway host's
/// started check reads and what the testing response reads for the same purpose.
/// </para>
/// </remarks>
internal sealed class CompressingResponseStream : Stream {
    private readonly IExecutionContext _context;
    private readonly Stream _transport;
    private readonly string _coding;
    private readonly ICompressionPredicate? _predicate;
    private readonly ICompressionConfiguration _configuration;
    private Stream? _target;
    private Stream? _encoder;
    private long _accepted;

    public CompressingResponseStream(
        IExecutionContext context,
        Stream transport,
        string coding,
        ICompressionPredicate? predicate,
        ICompressionConfiguration configuration) {
        _context = context;
        _transport = transport;
        _coding = coding;
        _predicate = predicate;
        _configuration = configuration;
    }

    /// <summary>The coding negotiated at filter entry, whether or not it ends up applied.</summary>
    public string Coding => _coding;

    /// <summary>Whether the first write opened an encoder. False until then.</summary>
    public bool IsCompressing => _encoder != null;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _accepted;

    public override long Position {
        get => _accepted;
        set => throw new NotSupportedException();
    }

    public override void Flush() => (_target ?? _transport).Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        (_target ?? _transport).FlushAsync(cancellationToken);

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
    /// Writes the trailer. Never closes the transport, which the host completes.
    /// </summary>
    public override async ValueTask DisposeAsync() {
        if (_encoder != null) {
            await _encoder.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _encoder?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Where the bytes go, chosen once.
    /// </summary>
    private Stream Target() {
        if (_target != null) {
            return _target;
        }

        var response = _context.Response;

        if (!Compresses(response)) {
            return _target = _transport;
        }

        response.Headers[KnownHeaders.ContentEncoding] = _coding;
        VaryHeader.Add(response.Headers, KnownHeaders.AcceptEncoding);

        // Whatever was announced measured the identity bytes. The transport frames what is
        // actually written.
        response.Headers.Remove(KnownHeaders.ContentLength);

        // A strong validator names one exact byte sequence, and these are not those bytes. Weak
        // keeps the validator usable for a conditional request while saying so, which is what
        // ASP.NET Core's compression does with the same header.
        if (response.Headers.TryGetValue(KnownHeaders.ETag, out var etag) &&
            etag.Count == 1 && etag[0] is { Length: > 0 } strong &&
            !strong.StartsWith("W/", StringComparison.Ordinal)) {
            response.Headers[KnownHeaders.ETag] = "W/" + strong;
        }

        // For the API Gateway host, which base64-encodes a binary body and would otherwise decode
        // these bytes as UTF-8.
        response.IsBinary = true;

        _encoder = string.Equals(_coding, KnownEncoding.Br, StringComparison.OrdinalIgnoreCase)
            ? new BrotliStream(_transport, _configuration.Level, leaveOpen: true)
            : new GZipStream(_transport, _configuration.Level, leaveOpen: true);

        return _target = _encoder;
    }

    /// <summary>
    /// The decision, in order: a response that has started or already carries a coding is left
    /// alone; so is a status with no body or a byte range, since an offset into a compressed
    /// stream is not an offset into the resource; then the operation's predicate over the
    /// handler's value where there is one, and the configured media-type rule otherwise.
    /// </summary>
    private bool Compresses(IExecutionResponse response) {
        if (response.ResponseStarted || response.Headers.ContainsKey(KnownHeaders.ContentEncoding)) {
            return false;
        }

        if (response.Status is 204 or 206 or 304) {
            return false;
        }

        // A hit replayed from the response cache carries no handler value, so a predicate is not
        // consulted there and the default rule applies. See ICompressionPredicate.
        if (_predicate != null && response.ResponseValue is { } value) {
            return _predicate.ShouldCompress(value, _context);
        }

        return _configuration.Compresses(response.ContentType);
    }
}
