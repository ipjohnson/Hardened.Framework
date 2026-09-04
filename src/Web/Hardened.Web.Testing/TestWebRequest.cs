using System.Text;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// Represents a web request
/// </summary>
public class TestWebRequest {
    /// <summary>
    /// Headers for request
    /// </summary>
    public IDictionary<string, StringValues> Headers { get; set; } = default!;

    /// <summary>
    /// CancellationToken for the request
    /// </summary>
    public CancellationToken? Token { get; set; }

    /// <summary>
    /// The body as bytes, sent exactly as given. Set through <see cref="RawBody(byte[], string)"/>
    /// or <see cref="RawBody(string, string)"/>; it takes precedence over the value the call was
    /// made with.
    /// </summary>
    public byte[]? Body { get; set; }

    /// <summary>
    /// A body the deserializer sees as written, with the content type it is declared under.
    /// </summary>
    /// <remarks>
    /// For a malformed document, a truncated payload, or a shape the serializer would never
    /// produce - the requests a client library cannot be made to send and a validation status
    /// cannot be asserted without.
    /// </remarks>
    public TestWebRequest RawBody(string text, string contentType = KnownContentType.Json) =>
        RawBody(Encoding.UTF8.GetBytes(text), contentType);

    public TestWebRequest RawBody(byte[] bytes, string contentType) {
        Body = bytes;
        Headers[KnownHeaders.ContentType] = contentType;

        return this;
    }
}
