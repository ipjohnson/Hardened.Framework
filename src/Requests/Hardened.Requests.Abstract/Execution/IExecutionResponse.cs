using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionResponse {
    IExecutionResponse Clone(IHeaderCollection? headerCollection = null);

    string? ContentType { get; set; }

    object? ResponseValue { get; set; }

    /// <summary>
    /// Builds what writes this response, or null when it is serialized like any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assigned by the generated handler from <c>[Output&lt;T&gt;]</c> before the handler runs, so a
    /// handler or filter can replace it - a different view for mobile than for desktop, an A/B
    /// test, an error view. Setting it takes the response out of negotiation: the output either
    /// answers what the client asked for or the request gets a 406.
    /// </para>
    /// <para>
    /// A factory rather than an instance so nothing is allocated for a response that is never
    /// written - an exception path, a short-circuiting filter, a 304.
    /// </para>
    /// </remarks>
    Func<IExecutionContext, IHardenedResponseOutput>? OutputFactory { get; set; }

    /// <summary>
    /// The output once built, or null before anything has needed it.
    /// </summary>
    /// <remarks>
    /// Built on first use and kept, because it is asked whether it answers the request before it is
    /// asked to write. A filter may also read or replace it.
    /// </remarks>
    IHardenedResponseOutput? Output { get; set; }

    int? Status { get; set; }

    bool ShouldCompress { get; set; }

    Stream Body { get; set; }

    IDictionary<string, StringValues> Headers { get; }

    Exception? ExceptionValue { get; set; }

    bool ResponseStarted { get; }

    bool IsBinary { get; set; }

    ICookieSetCollection Cookies { get; }

    /// <summary>
    /// Whether this response still needs turning into bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set false to opt out - a 405 whose whole answer is a status and a header, a handler that
    /// wrote the body itself - and cleared by whatever serializes the response once it has.
    /// </para>
    /// <para>
    /// The "once it has" half is what lets <c>ResponseFinalizerFilter</c> cover a middleware that
    /// answered without ever entering a handler chain, without writing an ordinary response a
    /// second time on the way back out. Anything that serializes must clear it.
    /// </para>
    /// </remarks>
    bool ShouldSerialize { get; set; }
}