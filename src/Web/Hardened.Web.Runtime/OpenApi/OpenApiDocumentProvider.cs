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
/// Serves a generated OpenAPI document at a fixed path.
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
/// </remarks>
public class OpenApiDocumentProvider : IWebExecutionRequestHandlerProvider {
    private readonly string _path;
    private readonly OpenApiDocumentHandler _handler;

    public OpenApiDocumentProvider(string document, string path = "/openapi.json") {
        _path = path;
        _handler = new OpenApiDocumentHandler(document, path);
    }

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        if (!string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(context.Request.Path, _path, StringComparison.Ordinal)) {
            return null;
        }

        return new RequestHandlerInfo(_handler, PathTokenCollection.Empty);
    }

    private class OpenApiDocumentHandler : IExecutionRequestHandler {
        private readonly byte[] _document;

        public OpenApiDocumentHandler(string document, string path) {
            _document = Encoding.UTF8.GetBytes(document);

            HandlerInfo = new ExecutionRequestHandlerInfo(
                path, "GET", typeof(OpenApiDocumentProvider), nameof(GetExecutionChain));
        }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            new ExecutionChain(new Func<IExecutionContext, IExecutionFilter>[] { _ => new WriteFilter(_document) }, context);
    }

    /// <summary>
    /// Writes the bytes directly rather than going through serialization: the document is already
    /// JSON, and handing it to a serializer would encode it a second time as a string.
    /// </summary>
    private class WriteFilter : IExecutionFilter {
        private readonly byte[] _document;

        public WriteFilter(byte[] document) {
            _document = document;
        }

        public async Task Execute(IExecutionChain chain) {
            var response = chain.Context.Response;

            response.Status = 200;
            response.ContentType = "application/json";
            response.ShouldSerialize = false;
            response.Headers[KnownHeaders.CacheControl] = new StringValues("no-cache");

            await response.Body.WriteAsync(_document, 0, _document.Length);
        }
    }
}
