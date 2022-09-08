﻿using System.IO.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Hardened.Web.Runtime.Headers;

namespace Hardened.Web.Testing
{

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
                if (contentEncoding.Contains("gzip"))
                {
                    using var gzipStream = new GZipStream(Body, CompressionMode.Decompress, true);

                    return System.Text.Json.JsonSerializer.Deserialize<T>(gzipStream) ??
                           throw new Exception("Could not deserialize response");
                }

                if (contentEncoding.Contains("br"))
                {
                    using var brStream = new BrotliStream(Body, CompressionMode.Decompress, true);

                    return System.Text.Json.JsonSerializer.Deserialize<T>(brStream) ??
                           throw new Exception("Could not deserialize response");
                }

                throw new BadContentEncodingException(contentEncoding);
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(Body) ??
                   throw new Exception("Could not deserialize response");
        }
    }
}