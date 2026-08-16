using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared response conformance suite against the ASP.NET Core adapter.
/// </summary>
/// <remarks>
/// Driven through a real <see cref="DefaultHttpContext"/>, so what is under test is the mapping
/// onto <c>HttpResponse</c> — the object ASP.NET actually sends from. This is the transport that
/// disagreed with the harness about every response with no body.
/// </remarks>
public class AspNetExecutionResponseConformanceTests : ExecutionResponseConformanceTests {

    protected override IExecutionResponseConformanceAdapter Adapter { get; } = new AspNetAdapter();

    /// <summary>
    /// Holds the <see cref="HttpContext"/> it built, which is safe because xUnit constructs a new
    /// test class instance — and so a new adapter — for every test method. It also means a clone
    /// completes against the same <c>HttpResponse</c> as the response it was cloned from, which is
    /// the behaviour a fork depends on.
    /// </summary>
    private class AspNetAdapter : IExecutionResponseConformanceAdapter {
        private HttpContext? _httpContext;

        public string TransportName => "ASP.NET Core";

        public IExecutionResponse CreateResponse() {
            _httpContext = new DefaultHttpContext();

            // DefaultHttpContext gives Stream.Null, which accepts writes and keeps nothing.
            _httpContext.Response.Body = new MemoryStream();

            return new AspNetExecutionResponse(_httpContext.Response);
        }

        /// <summary>
        /// ASP.NET writes status, headers and body straight through to <c>HttpResponse</c> as they
        /// are set, and flushes when the middleware unwinds. So completion is reading the response
        /// object the host would have sent — including <c>StatusCode</c>, which is 200 until
        /// something says otherwise and is the reason an unset status is not a missing one.
        /// </summary>
        public Task<ObservedResponse> Complete(IExecutionResponse response) {
            var httpResponse = _httpContext!.Response;
            var body = (MemoryStream)httpResponse.Body;

            var headers = httpResponse.Headers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.ToArray()!,
                StringComparer.OrdinalIgnoreCase);

            headers.TryGetValue("Set-Cookie", out var setCookies);

            return Task.FromResult(new ObservedResponse(
                httpResponse.StatusCode,
                headers,
                setCookies ?? Array.Empty<string>(),
                body.ToArray()));
        }
    }
}
