using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// A request whose <c>Content-Type</c> the route does not read - 415.
/// </summary>
/// <remarks>
/// With an <c>Accept</c> header naming the types the route reads, which is what RFC 9110 suggests
/// for an unsupported media type. <see cref="BadContentEncodingException"/> does the same with
/// <c>Accept-Encoding</c> for an unsupported coding.
/// </remarks>
public class UnsupportedContentTypeException : StatusCodeException
{
    /// <param name="contentType">The <c>Content-Type</c> the request carried.</param>
    /// <param name="supported">The types the route reads, as the <c>Accept</c> header lists them.</param>
    public UnsupportedContentTypeException(string contentType, string supported)
        : base(
            415,
            value: null,
            message: $"This route does not read {contentType}. It reads {supported}."
        )
    {
        ContentType = contentType;
        Supported = supported;
    }

    /// <summary>The <c>Content-Type</c> the request carried.</summary>
    public string ContentType { get; }

    /// <summary>The types the route reads.</summary>
    public string Supported { get; }

    public override void ApplyHeaders(IDictionary<string, StringValues> headers)
    {
        headers[KnownHeaders.Accept] = Supported;
    }
}
