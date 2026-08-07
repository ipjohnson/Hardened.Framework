namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// Thrown when a request arrives with a Content-Encoding the deserializers do not support.
/// That is a client error, so it derives from <see cref="BadRequestException"/> and is
/// classified as a 400 by type rather than by the shape of its name.
/// </summary>
public class BadContentEncodingException : BadRequestException {
    public BadContentEncodingException(string contentEncoding) : base(
        $"{contentEncoding} is not a supported Content-Encoding") { }
}
