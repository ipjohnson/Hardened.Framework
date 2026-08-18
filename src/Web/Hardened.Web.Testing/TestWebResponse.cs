using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

public class TestWebResponse {
    private readonly IExecutionResponse _executionResponse;
    private IWebAssertThat? _assertThat;

    public TestWebResponse(IExecutionResponse executionResponse) {
        _executionResponse = executionResponse;
    }

    public int StatusCode => _executionResponse.Status.GetValueOrDefault(200);

    public IDictionary<string, StringValues> Headers => _executionResponse.Headers;

    public Stream Body => _executionResponse.Body;

    public IWebAssertThat Assert => _assertThat ??= new WebAssertThat(this);

    public async IAsyncEnumerable<T> DeserializeAsyncEnumerable<T>() {
        Body.Position = 0;
        using var reader = new StreamReader(Body, leaveOpen: true);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        while (await reader.ReadLineAsync() is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            yield return JsonSerializer.Deserialize<T>(line, options) ??
                         throw new Exception("Could not deserialize NDJSON line");
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