using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.CacheControl;

/// <summary>
/// Writes the <c>Cache-Control</c> header a handler's <c>[CacheControl]</c> declares.
/// </summary>
/// <remarks>
/// The header is set before <c>chain.Next()</c> rather than after, so it is in place by the time
/// the IO filter serializes at <c>FilterOrder.Serialization</c>. Setting it afterwards would be
/// too late on a transport that has already begun writing the response.
/// </remarks>
public class CacheControlFilter : IExecutionFilter {
    private readonly string _headerValue;

    public CacheControlFilter(string headerValue) {
        _headerValue = headerValue;
    }

    public Task Execute(IExecutionChain chain) {
        chain.Context.Response.Headers[KnownHeaders.CacheControl] = new StringValues(_headerValue);

        return chain.Next();
    }
}
