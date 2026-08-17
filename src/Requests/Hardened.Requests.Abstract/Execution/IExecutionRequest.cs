using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionRequest {
    IExecutionRequest Clone(
        string? method = null,
        string? path = null,
        IDictionary<string, StringValues>? headers = null,
        IQueryStringCollection? queryString = null,
        IReadOnlyList<string>? cookies = null
    );

    string Method { get; }

    string Path { get; }

    string? ContentType { get; }

    string? Accept { get; }

    IExecutionRequestParameters? Parameters { get; set; }

    Stream Body { get; set; }

    IDictionary<string, StringValues> Headers { get; }

    IQueryStringCollection QueryString { get; }

    IPathTokenCollection PathTokens { get; set; }

    IReadOnlyList<string> Cookies { get; }

    /// <summary>
    /// What the transport knows about the connection this arrived on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never null. A transport with nothing to say - an in-memory harness, a queue record - answers
    /// <c>EmptyTransportInfo.Instance</c>, so a caller asks for a fact and gets null rather than
    /// asking whether there is a transport to ask.
    /// </para>
    /// <para>
    /// <b>Deliberately not properties.</b> Every transport knows a different subset, and a property
    /// per fact would grow this interface every time a host is added while being null on most of
    /// them. The keys are OpenTelemetry's, so the same fact reads the same under Lambda as under
    /// Kestrel - see <see cref="KnownTransportKeys"/>.
    /// </para>
    /// <para>
    /// <b>Carried through <see cref="Clone"/> unchanged.</b> A forked chain is the same request on
    /// the same connection; rebinding its method or path says nothing about where it came from.
    /// </para>
    /// </remarks>
    ITransportInfo Transport { get; }
}