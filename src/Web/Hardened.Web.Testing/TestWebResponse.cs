using System.IO.Compression;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;

namespace Hardened.Web.Testing;

public class TestWebResponse
{
    private readonly IExecutionResponse _executionResponse;
    private IWebAssertThat? _assertThat;
    public TestWebResponse(IExecutionResponse executionResponse)
    {
        _executionResponse = executionResponse;
    }

    public int StatusCode => _executionResponse.Status.GetValueOrDefault(200);

    public IHeaderCollection Headers => _executionResponse.Headers;

    public Stream Body => _executionResponse.Body;

    public IWebAssertThat Assert => _assertThat ??= new WebAssertThat(this);

    public T Deserialize<T>()
    {
        if (Headers.TryGet(KnownHeaders.ContentEncoding, out var contentEncoding))
        {
            if (contentEncoding.Contains(KnownEncoding.GZip))
            {
                using var gzipStream = new GZipStream(Body, CompressionMode.Decompress, true);

                return System.Text.Json.JsonSerializer.Deserialize<T>(gzipStream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                       throw new Exception("Could not deserialize response");
            }

            if (contentEncoding.Contains(KnownEncoding.Br))
            {
                using var brStream = new BrotliStream(Body, CompressionMode.Decompress, true);

                return System.Text.Json.JsonSerializer.Deserialize<T>(brStream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
                       throw new Exception("Could not deserialize response");
            }

            throw new BadContentEncodingException(contentEncoding);
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
               throw new Exception("Could not deserialize response");
    }
}