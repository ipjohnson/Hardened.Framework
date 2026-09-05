using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

public class TestWebResponse {
    private readonly int _status;
    private readonly IDictionary<string, StringValues> _headers;
    private readonly Stream _body;
    private readonly Exception? _failure;
    private IWebAssertThat? _assertThat;

    public TestWebResponse(IExecutionResponse executionResponse)
        : this(executionResponse.Status.GetValueOrDefault(200), executionResponse.Headers, executionResponse.Body, executionResponse.ExceptionValue) {
    }

    /// <summary>
    /// What a socket host answered: the status, every header as the wire carried it, the body
    /// bytes, and no failure, because an exception does not cross a wire.
    /// </summary>
    internal TestWebResponse(int status, IDictionary<string, StringValues> headers, Stream body, Exception? failure) {
        _status = status;
        _headers = headers;
        _body = body;
        _failure = failure;
    }

    public int StatusCode => _status;

    public IDictionary<string, StringValues> Headers => _headers;

    public Stream Body => _body;

    /// <summary>
    /// What the pipeline recorded when it refused or failed the request, or null.
    /// </summary>
    /// <remarks>
    /// A failure ahead of serialization, a handler that threw and a handler the container could not
    /// build all reach the client as the error envelope, whose 500 body says nothing about the
    /// cause on purpose. This is the cause, for a test asserting which failure it was and what it
    /// named. Null on a socket host, where the envelope is all that crosses the wire.
    /// </remarks>
    public Exception? Failure => _failure;

    public IWebAssertThat Assert => _assertThat ??= new WebAssertThat(this);

    /// <summary>
    /// The items of an NDJSON body, decoded first when the response says it is compressed - the
    /// same as <see cref="Deserialize{T}"/>, and for the same reason: a test asserting on the
    /// items should not have to know whether the handler was compressed.
    /// </summary>
    public async IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>() {
        Body.Position = 0;

        var decoded = Decode();

        try {
            using var reader = new StreamReader(decoded, leaveOpen: true);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            while (await reader.ReadLineAsync() is { } line) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                yield return JsonSerializer.Deserialize<T>(line, options) ??
                             throw new Exception("Could not deserialize NDJSON line");
            }
        }
        finally {
            if (!ReferenceEquals(decoded, Body)) {
                decoded.Dispose();
            }
        }
    }

    public T Deserialize<T>() {
        var decoded = Decode();

        try {
            return System.Text.Json.JsonSerializer.Deserialize<T>(decoded,
                       new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                   throw new Exception("Could not deserialize response");
        }
        finally {
            if (!ReferenceEquals(decoded, Body)) {
                decoded.Dispose();
            }
        }
    }

    /// <summary>
    /// The whole body as text, with <c>Content-Encoding</c> undone.
    /// </summary>
    /// <remarks>
    /// <c>TestWebApp</c> sends <c>Accept-Encoding: gzip</c> on every request, so a handler that
    /// honours it answers compressed and a test reading <see cref="Body"/> directly gets the gzip
    /// magic number rather than its content. That is what a real client would receive too - it just
    /// decompresses before anyone sees it. <see cref="Deserialize{T}"/> already did this for JSON;
    /// this is the same for anything that is not - a YAML specification, a rendered page.
    /// </remarks>
    public async Task<string> ReadTextAsync() {
        Body.Position = 0;

        var decoded = Decode();

        try {
            using var reader = new StreamReader(decoded, Encoding.UTF8, leaveOpen: true);

            return await reader.ReadToEndAsync();
        }
        finally {
            if (!ReferenceEquals(decoded, Body)) {
                decoded.Dispose();
            }
        }
    }

    /// <summary>
    /// <see cref="Body"/> with any content coding undone, or <see cref="Body"/> itself when there is
    /// none. The caller disposes the result only when it is not <see cref="Body"/>, which the
    /// response still owns.
    /// </summary>
    private Stream Decode() {
        if (!Headers.TryGetValue(KnownHeaders.ContentEncoding, out var contentEncoding)) {
            return Body;
        }

        if (contentEncoding.Contains(KnownEncoding.GZip)) {
            return new GZipStream(Body, CompressionMode.Decompress, true);
        }

        if (contentEncoding.Contains(KnownEncoding.Br)) {
            return new BrotliStream(Body, CompressionMode.Decompress, true);
        }

        throw new BadContentEncodingException(contentEncoding.ToString());
    }
}