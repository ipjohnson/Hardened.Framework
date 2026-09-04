using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// A request arrived with a <c>Content-Encoding</c> the request filter does not decode - 415.
/// </summary>
/// <remarks>
/// <para>
/// 415 with an <c>Accept-Encoding</c> header naming what is supported, which is what RFC 9110
/// specifies for an unsupported content coding. It was a 400 while the JSON deserializers did the
/// decoding, and a test pinned that; <c>RequestDecompressionFilter</c> changed both together.
/// </para>
/// <para>
/// A <see cref="StatusCodeException"/> rather than a <see cref="BadRequestException"/>, because the
/// status is not well-formed without the header and that base is the one that can write one.
/// </para>
/// </remarks>
public class BadContentEncodingException : StatusCodeException {
    /// <summary>What a client may send instead. The request filter decodes exactly these.</summary>
    public const string SupportedEncodings = KnownEncoding.GZip + ", " + KnownEncoding.Br;

    public BadContentEncodingException(string contentEncoding) : base(
        415, value: null, message: $"{contentEncoding} is not a supported Content-Encoding") { }

    public override void ApplyHeaders(IDictionary<string, StringValues> headers) {
        headers[KnownHeaders.AcceptEncoding] = SupportedEncodings;
    }
}
