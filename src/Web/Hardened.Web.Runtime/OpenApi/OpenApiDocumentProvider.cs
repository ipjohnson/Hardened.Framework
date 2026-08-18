using System.Globalization;
using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.OpenApi;

/// <summary>
/// Serves an OpenAPI document at a fixed path.
/// </summary>
/// <remarks>
/// <para>
/// Registered as one more <c>IWebExecutionRequestHandlerProvider</c>, which is how the routing
/// tables themselves are registered - the pipeline asks each in turn, so this needs no route
/// attribute and no entry in any generated table.
/// </para>
/// <para>
/// The providers are consulted in reverse registration order, so an application that declares its
/// own route at this path wins. That is deliberate: a framework-supplied endpoint should never
/// shadow one somebody wrote.
/// </para>
/// <para>
/// <b>The document is held compressed.</b> A code-first application's is compressed already, by the
/// generator that wrote it; a specification-first one is compressed here, once, when this is
/// constructed. Practically every client sends <c>Accept-Encoding: gzip</c>, so the common path
/// writes those bytes untouched and compresses nothing per request. A client that does not ask is
/// served the document inflated on the way out.
/// </para>
/// </remarks>
public class OpenApiDocumentProvider : IWebExecutionRequestHandlerProvider {
    private readonly string _path;
    private readonly OpenApiDocumentHandler _handler;

    /// <summary>
    /// Serves the generated document, which arrives gzip-compressed.
    /// </summary>
    /// <param name="gzipDocument">
    /// The compressed document. Taken as a span because that is what the generator emits - a
    /// <c>ReadOnlySpan&lt;byte&gt;</c> over a metadata blob, with nothing allocated to hold it - and
    /// copied once here, since a span cannot be stored in a field or cross an await.
    /// </param>
    public OpenApiDocumentProvider(
        ReadOnlySpan<byte> gzipDocument, string path = "/openapi.json",
        string contentType = "application/json") {
        _path = path;
        _handler = new OpenApiDocumentHandler(gzipDocument.ToArray(), path, contentType);
    }

    /// <summary>
    /// Serves <paramref name="document"/> at <paramref name="path"/>.
    /// </summary>
    /// <param name="contentType">
    /// What the document is written in. Defaults to JSON, which is what the generated document is
    /// and what the default path implies. A specification-first application serves its source
    /// specification verbatim, and that may be YAML - converting it would put an emitter back in a
    /// path that exists to have none.
    /// </param>
    /// <remarks>
    /// Compresses at construction rather than per request. That costs a few milliseconds once, at
    /// startup, and it is the only way a specification-first document - which is embedded as source
    /// text, because embedding it any other way would mean re-emitting it - reaches the wire at the
    /// same size a generated one does.
    /// </remarks>
    public OpenApiDocumentProvider(
        string document, string path = "/openapi.json", string contentType = "application/json") {
        _path = path;
        _handler = new OpenApiDocumentHandler(Compress(document), path, contentType);
    }

    /// <summary>What a request to this path may do, when it did something else.</summary>
    private const string Allow = "GET, HEAD";

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        if (!string.Equals(context.Request.Path, _path, StringComparison.Ordinal)) {
            return null;
        }

        // HEAD as well as GET. The table's usual HEAD-to-GET redirection does not reach a provider
        // serving its own chain, but WebExecutionHandlerService.Dispatch still drops the body and
        // reports the length for one - so accepting it here is all that was missing.
        var method = context.Request.Method;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            // The path is checked first so a write to the document answers 405 rather than 404: the
            // resource exists, and that distinction is what a client and a CDN both read.
            return RequestHandlerInfo.MethodNotAllowed(Allow);
        }

        return new RequestHandlerInfo(_handler, PathTokenCollection.Empty);
    }

    private static byte[] Compress(string document) {
        var bytes = Encoding.UTF8.GetBytes(document);

        using var output = new MemoryStream();

        // Disposed before the buffer is read: GZipStream writes its footer on dispose, so a buffer
        // taken while it is still open holds a truncated member.
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private class OpenApiDocumentHandler : IExecutionRequestHandler {
        private readonly byte[] _gzipDocument;
        private readonly string _contentType;

        public OpenApiDocumentHandler(byte[] gzipDocument, string path, string contentType) {
            _gzipDocument = gzipDocument;
            _contentType = contentType;

            HandlerInfo = new ExecutionRequestHandlerInfo(
                path, "GET", typeof(OpenApiDocumentProvider), nameof(GetExecutionChain));
        }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            new ExecutionChain(
                new Func<IExecutionContext, IExecutionFilter>[] {
                    _ => new WriteFilter(_gzipDocument, _contentType)
                },
                context);
    }

    /// <summary>
    /// Writes the bytes directly rather than going through serialization: the document is already
    /// JSON, and handing it to a serializer would encode it a second time as a string.
    /// </summary>
    private class WriteFilter : IExecutionFilter {
        private readonly byte[] _gzipDocument;
        private readonly string _contentType;

        public WriteFilter(byte[] gzipDocument, string contentType) {
            _gzipDocument = gzipDocument;
            _contentType = contentType;
        }

        public async Task Execute(IExecutionChain chain) {
            var context = chain.Context;
            var response = context.Response;

            response.Status = 200;
            response.ContentType = _contentType;
            response.ShouldSerialize = false;
            response.Headers[KnownHeaders.CacheControl] = new StringValues("no-cache");

            if (AcceptsGZip(context)) {
                response.IsBinary = true;
                response.Headers[KnownHeaders.ContentEncoding] = KnownEncoding.GZipStringValues;
                response.Headers[KnownHeaders.ContentLength] =
                    _gzipDocument.Length.ToString(CultureInfo.InvariantCulture);

                await response.Body.WriteAsync(_gzipDocument, 0, _gzipDocument.Length);

                return;
            }

            using var source = new MemoryStream(_gzipDocument, writable: false);
            await using var gzip = new GZipStream(source, CompressionMode.Decompress);

            await gzip.CopyToAsync(response.Body);
        }

        /// <summary>
        /// Whether the client said it takes gzip.
        /// </summary>
        /// <remarks>
        /// The bounded token search this used to spell out inline now lives in
        /// <see cref="AcceptEncodingHeader"/>, unchanged. It was the only correct reading of
        /// <c>Accept-Encoding</c> in the codebase and it was private to this file, while
        /// <c>StaticContentHandler</c> a few directories away asked the question with
        /// <c>StringValues.Contains</c> and got the wrong answer for every browser.
        /// </remarks>
        private static bool AcceptsGZip(IExecutionContext context) =>
            context.Request.Headers.TryGetValue(KnownHeaders.AcceptEncoding, out var accepted) &&
            AcceptEncodingHeader.Accepts(accepted, KnownEncoding.GZip);
    }
}
