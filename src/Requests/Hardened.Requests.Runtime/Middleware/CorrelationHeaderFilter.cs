using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Middleware;

/// <summary>
/// Puts the request's correlation id on the response, so the caller can quote it.
/// </summary>
/// <remarks>
/// <para>
/// An id nobody outside the process can see only helps whoever is already reading the logs. The
/// value of this one is that a client, a support ticket or a failing test can name the request, and
/// that requires it to come back.
/// </para>
/// <para>
/// <b>Set on the way in, not on the way out.</b> A filter that annotated the response after
/// <c>Next</c> would miss every short circuit - a rate limiter's 429 and a rejected CORS preflight
/// are exactly the responses somebody wants to ask about - and would also be too late on a
/// transport that has already begun writing. Setting it first costs one header assignment and is
/// correct on every path.
/// </para>
/// <para>
/// Reading <c>CorrelationId</c> here is also what realizes it, which is deliberate: this runs after
/// the host has called <c>RequestBegin</c>, so a span exists if one is going to and the id is that
/// span's trace id rather than an unrelated value minted a moment earlier.
/// </para>
/// </remarks>
public class CorrelationHeaderFilter : IExecutionFilter {

    /// <summary>
    /// The header the id comes back on.
    /// </summary>
    /// <remarks>
    /// No standard exists. <c>X-Correlation-Id</c> is the spelling most log aggregators already know
    /// how to pick up, and it does not collide with the <c>traceparent</c> a trace-aware caller is
    /// separately entitled to read.
    /// </remarks>
    public const string HeaderName = "X-Correlation-Id";

    private readonly string _headerName;

    public CorrelationHeaderFilter(string? headerName = null) {
        _headerName = string.IsNullOrEmpty(headerName) ? HeaderName : headerName;
    }

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var correlationId = context.CorrelationId;

        if (!string.IsNullOrEmpty(correlationId)) {
            context.Response.Headers[_headerName] = new StringValues(correlationId);
        }

        return chain.Next();
    }
}
