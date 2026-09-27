using System.Net;

namespace Hardened.Web.Testing;

/// <summary>
/// A response's content, passed through unchanged, recording the body when the client buffers it.
/// </summary>
/// <remarks>
/// A client that buffers, which <see cref="HttpClient"/> does unless the request is sent with
/// <c>HttpCompletionOption.ResponseHeadersRead</c>, reads the content through
/// <c>SerializeToStreamAsync</c>, so the body is recorded before its call returns. A client that
/// reads the content as a stream gets the transport's stream as it arrives, and nothing is recorded.
/// </remarks>
internal sealed class RecordedContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action<byte[]> _buffered;

    public RecordedContent(HttpContent inner, Action<byte[]> buffered)
    {
        _inner = inner;
        _buffered = buffered;

        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken
    )
    {
        var body = await _inner.ReadAsByteArrayAsync(cancellationToken);

        _buffered(body);

        await stream.WriteAsync(body, cancellationToken);
    }

    protected override Task<Stream> CreateContentReadStreamAsync() => _inner.ReadAsStreamAsync();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
