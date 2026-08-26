using System.Globalization;
using System.IO.Compression;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Writes one published document.
/// </summary>
/// <remarks>
/// <para>
/// A controller rather than the provider writing the response inline, so the document has the same
/// shape as any other handler - which is what <c>ExecutionHelper</c> is built to run, and what makes
/// the filter chain, conventions and authorization apply to it unchanged. The same reason
/// <see cref="OpenApiUiController"/> and <c>StaticContentController</c> exist.
/// </para>
/// <para>
/// Stateless, and a singleton for that reason. What varies between two published documents - the
/// bytes, the content type - is closed over by the lambda <see cref="OpenApiDocumentProvider"/>
/// hands to <c>ExecutionHelper</c>, not held here.
/// </para>
/// </remarks>
public class OpenApiDocumentController {

    /// <summary>
    /// Writes the bytes directly rather than going through serialization: the document is already
    /// JSON or YAML, and handing it to a serializer would encode it a second time as a string.
    /// </summary>
    public async Task Write(IExecutionContext context, byte[] gzipDocument, string contentType) {
        var response = context.Response;

        response.Status = 200;
        response.ContentType = contentType;
        response.ShouldSerialize = false;
        response.Headers[KnownHeaders.CacheControl] = new StringValues("no-cache");

        if (AcceptsGZip(context)) {
            response.IsBinary = true;
            response.Headers[KnownHeaders.ContentEncoding] = KnownEncoding.GZipStringValues;
            response.Headers[KnownHeaders.ContentLength] =
                gzipDocument.Length.ToString(CultureInfo.InvariantCulture);

            await response.Body.WriteAsync(gzipDocument, 0, gzipDocument.Length);

            return;
        }

        using var source = new MemoryStream(gzipDocument, writable: false);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress);

        await gzip.CopyToAsync(response.Body);
    }

    /// <summary>
    /// Whether the client said it takes gzip.
    /// </summary>
    /// <remarks>
    /// The bounded token search this used to spell out inline now lives in
    /// <see cref="AcceptEncodingHeader"/>, unchanged. It was the only correct reading of
    /// <c>Accept-Encoding</c> in the codebase and it was private to one file, while
    /// <c>StaticContentHandler</c> a few directories away asked the question with
    /// <c>StringValues.Contains</c> and got the wrong answer for every browser.
    /// </remarks>
    private static bool AcceptsGZip(IExecutionContext context) =>
        context.Request.Headers.TryGetValue(KnownHeaders.AcceptEncoding, out var accepted) &&
        AcceptEncodingHeader.Accepts(accepted, KnownEncoding.GZip);
}
