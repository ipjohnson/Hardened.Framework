using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Hardened.Web.Runtime.Responses;

namespace Hardened.Web.Testing;

/// <summary>
/// One request through the pipeline, written once for <see cref="TestWebApp"/> and
/// <see cref="PipelineHttpMessageHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both entry points turn a description of a request into an execution context, run the chain
/// <see cref="IMiddlewareService"/> composes, and hand the answer back. Path decoding, header
/// handling, the cookie header, the test credential and the recording behind
/// <see cref="LastResponse"/> are therefore decided here and nowhere else: a request that answered
/// one way through <c>app.Get</c> and another through an <c>HttpClient</c> would be the divergence
/// a harness exists not to have.
/// </para>
/// <para>
/// The request logger, the metric logger and the scope are handled the way a host handles them -
/// begun before the chain, ended and disposed in a <c>finally</c> - so a request that threw still
/// reports its end and leaks nothing.
/// </para>
/// </remarks>
internal static class PipelineRequest {

    /// <summary>
    /// The request as the pipeline will see it: the path decoded the way a transport decodes one,
    /// the query string parsed by the parser Kestrel uses, the <c>Cookie</c> header split onto the
    /// cookie collection, and the test credential applied where the caller set neither header.
    /// </summary>
    public static TestExecutionRequest CreateRequest(
        string method,
        string pathAndQuery,
        IDictionary<string, StringValues> headers,
        Stream body,
        TestCredential? credential) {
        var path = pathAndQuery;
        var questionMark = pathAndQuery.IndexOf('?');

        if (questionMark > -1) {
            path = pathAndQuery.Substring(0, questionMark);
        }

        // Decoded, because a transport hands one over decoded. Passing it through undecoded made
        // /events/%20 reach the handler as the literal three characters and match nothing, while
        // the same request over a socket decoded to whitespace and reached the validator. See
        // RequestPathDecoder for the rule and where it was measured.
        path = RequestPathDecoder.Decode(path);

        credential?.ApplyTo(headers);

        headers.TryGetValue(KnownHeaders.Accept, out var accept);

        var cookies = new List<string>();

        if (headers.TryGetValue(KnownHeaders.Cookie, out var cookieHeader)) {
            foreach (var pair in cookieHeader.ToString().Split(';')) {
                var trimmed = pair.Trim();

                if (trimmed.Length > 0) {
                    cookies.Add(trimmed);
                }
            }
        }

        return new TestExecutionRequest(
            method, path, accept.ToString(), QueryStringParser.ParseFromPath(pathAndQuery)) {
            Headers = headers,
            Cookies = cookies,
            Body = body
        };
    }

    /// <summary>
    /// Runs <paramref name="request"/> through the chain and returns the response, its body in
    /// <paramref name="responseBody"/> rewound to the start.
    /// </summary>
    public static async Task<IExecutionResponse> Run(
        IServiceProvider rootServiceProvider,
        TestExecutionRequest request,
        MemoryStream responseBody,
        CancellationToken cancellationToken) {
        var middlewareService = rootServiceProvider.GetRequiredService<IMiddlewareService>();
        var requestLogger = rootServiceProvider.GetRequiredService<IRequestLogger>();
        var metricLoggerProvider = rootServiceProvider.GetRequiredService<IMetricLoggerProvider>();

        // Resolved if the container has one, constructed from the two services this method already
        // requires if it does not. Asking for it with GetRequiredService made it a fifth mandatory
        // registration, and a harness caller builds its own container by hand - the conformance
        // suites in this repository register exactly the four above, so twenty-six of them stopped
        // with a resolution failure. The executor is a pure function of those two, so there is
        // nothing to configure and no reason to make a caller register it.
        var executor = rootServiceProvider.GetService<IRequestExecutor>()
                       ?? new RequestExecutor(middlewareService, requestLogger);

        var scope = rootServiceProvider.CreateScope();

        var response = new TestExecutionResponse(responseBody) {
            Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        };

        var context = new TestExecutionContext(
            rootServiceProvider,
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            response,
            cancellationToken,
            metricLoggerProvider.CreateLogger("test-session"));

        var chain = middlewareService.GetExecutionChain(context);

        executor.Begin(context);

        try {
            await chain.Next();
        }
        catch (Exception exception) when (!response.ResponseStarted) {
            // The answer both socket hosts give, for the reason they give it - see
            // HardenedHttpApplication.ProcessRequestAsync and AspNetCoreRequestHandler. This host
            // caught nothing, so a failure that got past IoFilter unwound out of app.Get and the
            // test read a raw exception where the same request over a socket read a 500. IoFilter
            // records parameter binding and the handler; what escapes it is the response writer
            // itself, which is where an unserializable payload throws.
            //
            // Only where nothing has been written. Once bytes are with the client a socket host
            // stops and lets the connection tear down, and this host has no connection to tear
            // down - so the exception reaching the caller is how a stream that failed after its
            // first event says so, and swallowing it into a 500 would describe a response the
            // client never got. Hence the filter rather than a branch inside: a rethrow here would
            // report this frame as the origin.
            response.Status = 500;

            // Only where nothing was recorded. A handler that threw is already on the response and
            // is the cause a test wants named; the envelope's own serializer failing afterwards is
            // a consequence, and overwriting would hide the first.
            response.ExceptionValue ??= exception;

            requestLogger.RequestFailed(context, exception);
        }
        finally {
            // In a finally because these ran as straight-line statements after the chain, so a
            // request that threw closed out nothing: no duration, no end, and the scope leaked.
            //
            // The close-out itself is IRequestExecutor's - the duration, the end line and the
            // disposal that makes a provider emit, which is what a harness assertion about what a
            // request emitted has to read. The chain above is still driven here rather than through
            // RunChain, because this host's answer to a throw is genuinely its own: see the filter
            // on the catch.
            executor.End(context);

            scope.Dispose();
        }

        responseBody.Position = 0;

        LastResponse.Record(response, responseBody.ToArray());

        return response;
    }
}
