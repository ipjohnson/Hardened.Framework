using System.IO.Compression;
using System.Text;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;

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
/// <b>Its chain is built by <see cref="ExecutionHelper"/>.</b> It used to build its own, holding one
/// filter that wrote the bytes - so nothing that hangs off a handler reached the published document:
/// no filters, no conventions, no authorization. <c>IGlobalFilterRegistry</c>, which is where
/// <c>AuthorizationFilterProvider</c> lives, is only consulted inside
/// <c>ExecutionHelper.CreateFilterArray</c>, so an application on default-deny - whose entire
/// premise is that an unannotated handler is denied rather than public - served its whole API
/// description anonymously and got no diagnostic saying so. The reference page at <c>/docs</c> was
/// gate-able and the document it renders was not.
/// </para>
/// <para>
/// The same move <see cref="OpenApiUiProvider"/> and <c>StaticContentMountProvider</c> already made,
/// for the same reason. A document that wants a policy of its own states it as its
/// <c>requirement</c>, which <see cref="IExecutionRequestHandlerInfo"/> documents as the
/// supported way for a handler registered by hand to say what it needs.
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
    private readonly byte[] _gzipDocument;
    private readonly string _contentType;
    private readonly Requirement? _requirement;
    private readonly IServiceProvider _serviceProvider;

    private IExecutionRequestHandler? _handler;

    /// <summary>
    /// Serves the generated document, which arrives gzip-compressed.
    /// </summary>
    /// <param name="gzipDocument">
    /// The compressed document. Taken as a span because that is what the generator emits - a
    /// <c>ReadOnlySpan&lt;byte&gt;</c> over a metadata blob, with nothing allocated to hold it - and
    /// copied once here, since a span cannot be stored in a field or cross an await.
    /// </param>
    /// <param name="requirement">
    /// What a caller must satisfy to read the document. Null inherits the application's posture,
    /// which is the same three behaviours <c>HardenedOpenApiUi</c> chose for the page: public where
    /// no authorization is configured, denied under default-deny, and gate-able by convention
    /// everywhere else.
    /// </param>
    public OpenApiDocumentProvider(
        IServiceProvider serviceProvider,
        ReadOnlySpan<byte> gzipDocument, string path = "/openapi.json",
        string contentType = "application/json", Requirement? requirement = null)
        : this(serviceProvider, gzipDocument.ToArray(), path, contentType, requirement) { }

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
        IServiceProvider serviceProvider,
        string document, string path = "/openapi.json",
        string contentType = "application/json", Requirement? requirement = null)
        : this(serviceProvider, Compress(document), path, contentType, requirement) { }

    private OpenApiDocumentProvider(
        IServiceProvider serviceProvider,
        byte[] gzipDocument, string path, string contentType, Requirement? requirement) {
        _serviceProvider = serviceProvider;
        _gzipDocument = gzipDocument;
        _path = path;
        _contentType = contentType;
        _requirement = requirement;
    }

    /// <summary>What a request to this path may do, when it did something else.</summary>
    private const string Allow = "GET, HEAD";

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        if (!string.Equals(context.Request.Path, _path, StringComparison.Ordinal)) {
            return null;
        }

        // HEAD as well as GET. WebExecutionHandlerService.Dispatch drops the body and reports the
        // length for one, so accepting it here is all that is needed.
        var method = context.Request.Method;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            // The path is checked first so a write to the document answers 405 rather than 404: the
            // resource exists, and that distinction is what a client and a CDN both read.
            return RequestHandlerInfo.MethodNotAllowed(Allow);
        }

        // Built once, lazily, rather than per request - conventions are asked per handler
        // construction, which is the contract they are written against.
        return new RequestHandlerInfo(
            _handler ??= new Handler(
                _serviceProvider, _gzipDocument, _path, _contentType, _requirement),
            PathTokenCollection.Empty);
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

    private sealed class Handler : BaseExecutionHandler<OpenApiDocumentController> {

        /// <summary>
        /// Empty, and load bearing. There is deliberately no <c>[AllowAnonymous]</c>: that is the one
        /// thing a convention cannot narrow, and without it the document inherits the application's
        /// posture rather than overriding it.
        /// </summary>
        private static readonly object[] Metadata = [];

        public Handler(
            IServiceProvider serviceProvider,
            byte[] gzipDocument, string path, string contentType, Requirement? requirement)
            : base(ExecutionHelper.AsyncStandardFilterEmptyParameters<OpenApiDocumentController>(
                serviceProvider,
                new ExecutionRequestHandlerInfo(
                    path, "GET", typeof(OpenApiDocumentController),
                    nameof(OpenApiDocumentController.Write), [], Metadata, requirement),
                // A lambda rather than a static method, because what varies between two published
                // documents is exactly what it closes over.
                (context, controller) => controller.Write(context, gzipDocument, contentType),
                ExecutionHelper.GetFilterInfo(Metadata))) { }
    }
}
