using System.Net;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from the application's pipeline, with no
/// socket.
/// </summary>
/// <remarks>
/// <para>
/// A generated client is an <see cref="HttpClient"/> consumer, and the harness has no port. This is
/// where the two meet: an <see cref="HttpRequestMessage"/> becomes an execution context - method,
/// path decoded the way Kestrel decodes it, query string, every header with every value, the
/// <c>Cookie</c> header onto the request's cookies, the body as the bytes it arrived as - the chain
/// <c>IMiddlewareService</c> composes runs, and the response comes back as an
/// <see cref="HttpResponseMessage"/> carrying the status, the headers, <c>Set-Cookie</c> from
/// <c>Response.Cookies</c>, the content type and the body bytes. Scheme and host are ignored: the
/// harness has neither, so a client that builds relative URLs resolves against whatever base
/// address it was given.
/// </para>
/// <para>
/// <b>Generator-agnostic, on purpose.</b> This package names no client generator - no Kiota, NSwag
/// or Refit type, no package reference - or every test project would carry it, opted out or not.
/// A Kiota client, an NSwag client, a Refit interface and a hand-written client all run through
/// the same handler and authenticate the same way, because the only thing any of them needs is an
/// <see cref="HttpClient"/>. <see cref="ITestWebApp.CreateHttpClient"/> is the usual way to get one;
/// this type is public so a test with nothing but a root <see cref="IServiceProvider"/> in hand can
/// build one too.
/// </para>
/// <para>
/// The request's cancellation token is the one the chain sees, so cancelling the
/// <see cref="HttpClient"/> call cancels the pipeline. What the pipeline answered is also kept for
/// <see cref="LastResponse"/>, whether it was a status the client threw on or one it swallowed.
/// </para>
/// </remarks>
public sealed class PipelineHttpMessageHandler : HttpMessageHandler {
    private readonly IServiceProvider _rootServiceProvider;
    private readonly TestCredential? _credential;

    /// <param name="rootServiceProvider">The application's root container, which the chain is resolved from.</param>
    public PipelineHttpMessageHandler(IServiceProvider rootServiceProvider)
        : this(rootServiceProvider, null) {
    }

    /// <param name="credential">
    /// Applied as the two test headers on every request that carries neither, so a client built
    /// for a test parameter authenticates as the parameter's attributes said without any code of
    /// its own.
    /// </param>
    public PipelineHttpMessageHandler(IServiceProvider rootServiceProvider, TestCredential? credential) {
        _rootServiceProvider = rootServiceProvider;
        _credential = credential;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) {
        var executionRequest = await CreateRequestAsync(request, _credential, cancellationToken);

        var body = new MemoryStream();

        var response = await PipelineRequest.Run(_rootServiceProvider, executionRequest, body, cancellationToken);

        return ToResponse(response, body, request);
    }

    /// <summary>
    /// The execution request for <paramref name="request"/>: what the conformance suite holds this
    /// transport's translation to.
    /// </summary>
    internal static async Task<TestExecutionRequest> CreateRequestAsync(
        HttpRequestMessage request, TestCredential? credential, CancellationToken cancellationToken) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers) {
            headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        Stream body = Stream.Null;

        if (request.Content != null) {
            foreach (var header in request.Content.Headers) {
                headers[header.Key] = new StringValues(header.Value.ToArray());
            }

            // The bytes as the client wrote them, never re-serialised: a malformed body has to
            // reach the deserializer malformed.
            body = new MemoryStream(await request.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        return PipelineRequest.CreateRequest(
            request.Method.Method, PathAndQuery(request), headers, body, credential);
    }

    /// <summary>
    /// The response message for what the pipeline answered: what the conformance suite observes.
    /// </summary>
    internal static HttpResponseMessage ToResponse(
        IExecutionResponse response, MemoryStream body, HttpRequestMessage? request) {
        var message = new HttpResponseMessage((HttpStatusCode)(response.Status ?? 200)) {
            RequestMessage = request
        };

        body.Position = 0;

        var content = new ByteArrayContent(body.ToArray());

        message.Content = content;

        foreach (var header in response.Headers) {
            // The content's own length, not whatever the pipeline wrote; the bytes are the length.
            if (string.Equals(header.Key, KnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var values = header.Value.ToArray();

            // A content header is refused on the message and belongs on the content; the
            // message's collection says which by refusing it.
            if (!message.Headers.TryAddWithoutValidation(header.Key, values)) {
                content.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }

        return message;
    }

    /// <summary>
    /// The path and query as a client would put them on the wire, escapes intact, so the same
    /// decoding a socket request gets applies here.
    /// </summary>
    private static string PathAndQuery(HttpRequestMessage request) {
        var uri = request.RequestUri;

        if (uri == null) {
            return "/";
        }

        if (uri.IsAbsoluteUri) {
            return uri.PathAndQuery;
        }

        var relative = uri.OriginalString;

        return relative.StartsWith("/", StringComparison.Ordinal) ? relative : "/" + relative;
    }
}
