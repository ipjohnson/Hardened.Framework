using System.IO.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Compression;

/// <summary>
/// Decodes a request body that arrived with a <c>Content-Encoding</c>, so everything downstream
/// reads identity bytes.
/// </summary>
/// <remarks>
/// <para>
/// Installed by the request module at <c>FilterOrder.Before + FilterOrder.ResponseCache</c> and
/// always on, because decoding a compressed body worked for every application before this filter
/// existed and has to keep working without an opt-in. The two JSON deserializers used to do it
/// themselves, so a form body, a Newtonsoft body or a raw body could not be sent compressed at all.
/// Ahead of the cache so <c>ByPayload</c> hashes identity bytes and a gzip body shares an entry
/// with its plain twin, and ahead of the bind so the deserializers never see the header.
/// </para>
/// <para>
/// <b>The decoded size is capped.</b> A few hundred bytes of gzip can decode to gigabytes, and the
/// host's request body limit is measured on the wire. Reading past
/// <see cref="ICompressionConfiguration.MaxDecompressedRequestBytes"/> throws a 413 from inside
/// the bind, which the serialization filter records and answers.
/// </para>
/// <para>
/// <b>A coding this filter does not know is a 415</b>, carrying <c>Accept-Encoding: gzip, br</c>
/// as RFC 9110 asks, and it is recorded on the response rather than thrown: everything ahead of
/// <c>FilterOrder.Serialization</c> refuses that way so the filter that writes the response is
/// still reached.
/// </para>
/// </remarks>
public sealed class RequestDecompressionFilter : IExecutionFilter {
    /// <summary>
    /// Resolved on the first request that carries a coding, for the reason the response cache
    /// resolves its store that way: there is no service provider where a filter is built.
    /// </summary>
    private ICompressionConfiguration? _configuration;

    /// <param name="configuration">
    /// Supplied by tests. Left null, it is read from the application's services on first use.
    /// </param>
    public RequestDecompressionFilter(ICompressionConfiguration? configuration = null) {
        _configuration = configuration;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var request = context.Request;
        var coding = ContentCoding(request.Headers);

        if (coding == null) {
            await chain.Next();

            return;
        }

        Stream decoder;

        if (string.Equals(coding, KnownEncoding.GZip, StringComparison.OrdinalIgnoreCase)) {
            decoder = new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);
        }
        else if (string.Equals(coding, KnownEncoding.Br, StringComparison.OrdinalIgnoreCase)) {
            decoder = new BrotliStream(request.Body, CompressionMode.Decompress, leaveOpen: true);
        }
        else {
            context.Response.ExceptionValue = new BadContentEncodingException(coding);

            await chain.Next();

            return;
        }

        var encoded = request.Body;

        request.Body = new BoundedReadStream(decoder, Configuration(context).MaxDecompressedRequestBytes);

        // Removed so nothing downstream decodes a second time, and Content-Length with it, because
        // it measured the bytes on the wire and no longer describes the body anything will read.
        request.Headers.Remove(KnownHeaders.ContentEncoding);
        request.Headers.Remove(KnownHeaders.ContentLength);

        try {
            await chain.Next();
        }
        finally {
            request.Body = encoded;

            await decoder.DisposeAsync();
        }
    }

    /// <summary>
    /// The coding the body is in, or null when there is none to undo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>identity</c> is the absence of a coding and is treated as one. A header naming several
    /// codings - <c>gzip, br</c> is a body compressed twice - names one this filter does not
    /// support, and is refused under the whole value so the 415 says what was sent.
    /// </para>
    /// <para>
    /// Looked up the way HTTP defines header names, because API Gateway delivers them lowercased
    /// and a forked request carries whatever dictionary it was handed.
    /// </para>
    /// </remarks>
    private static string? ContentCoding(IDictionary<string, StringValues> headers) {
        var value = Read(headers, KnownHeaders.ContentEncoding);

        if (StringValues.IsNullOrEmpty(value)) {
            return null;
        }

        string? coding = null;

        foreach (var element in value) {
            if (string.IsNullOrWhiteSpace(element)) {
                continue;
            }

            foreach (var token in element.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (string.Equals(token, "identity", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (coding != null) {
                    return value.ToString();
                }

                coding = token;
            }
        }

        return coding;
    }

    private ICompressionConfiguration Configuration(IExecutionContext context) {
        // Racy by construction and harmless: two requests may both resolve the same singleton.
        return _configuration ??= context.RootServiceProvider
            .GetRequiredService<IOptions<ICompressionConfiguration>>().Value;
    }

    private static StringValues Read(IDictionary<string, StringValues> headers, string name) {
        if (headers.TryGetValue(name, out var value)) {
            return value;
        }

        foreach (var header in headers) {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) {
                return header.Value;
            }
        }

        return StringValues.Empty;
    }
}
