using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Conformance;

/// <summary>
/// Runs the shared transport conformance suite against the ASP.NET Core adapter.
///
/// The adapter is exercised through a real <see cref="DefaultHttpContext"/> populated the
/// way Kestrel would populate it, so what is under test is genuinely the mapping from
/// ASP.NET Core's request onto <see cref="IExecutionRequest"/>.
/// </summary>
public class AspNetExecutionRequestConformanceTests : ExecutionRequestConformanceTests {

    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new AspNetAdapter();

    private class AspNetAdapter : IExecutionRequestConformanceAdapter {
        public string TransportName => "ASP.NET Core";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var httpContext = new DefaultHttpContext();
            var httpRequest = httpContext.Request;

            httpRequest.Method = spec.Method;
            httpRequest.Path = spec.Path;

            foreach (var header in spec.Headers) {
                httpRequest.Headers[header.Key] = header.Value;
            }

            if (spec.QueryString.Count > 0) {
                httpRequest.QueryString = QueryString.Create(
                    spec.QueryString.ToDictionary(q => q.Key, q => (string?)q.Value));
            }

            // Cookies arrive over the wire in a single Cookie header, which is how Kestrel
            // delivers them before ASP.NET parses them into HttpRequest.Cookies.
            if (spec.Cookies.Count > 0) {
                httpRequest.Headers.Cookie = string.Join("; ", spec.Cookies);
            }

            if (spec.Body is not null) {
                httpRequest.Body = new MemoryStream(spec.Body);
                httpRequest.ContentLength = spec.Body.Length;
            }

            return new AspNetExecutionRequest(httpRequest);
        }
    }
}
