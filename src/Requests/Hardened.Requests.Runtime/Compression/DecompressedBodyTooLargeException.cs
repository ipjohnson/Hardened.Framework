using System.Globalization;
using Hardened.Requests.Abstract.Errors;

namespace Hardened.Requests.Runtime.Compression;

/// <summary>
/// A compressed request body decoded past
/// <see cref="ICompressionConfiguration.MaxDecompressedRequestBytes"/> - 413.
/// </summary>
/// <remarks>
/// The limit is in the message, because a 413 that does not say how large is too large leaves the
/// caller guessing. Thrown from inside the bind, where the decoder is being read, so it reaches the
/// caller through the same path as any other failure to read the body.
/// </remarks>
public class DecompressedBodyTooLargeException : StatusCodeException {
    public DecompressedBodyTooLargeException(long limit) : base(
        413,
        value: null,
        message: "The request body decodes to more than " +
                 limit.ToString(CultureInfo.InvariantCulture) + " bytes.") {
        Limit = limit;
    }

    /// <summary>The cap that was exceeded, in bytes.</summary>
    public long Limit { get; }
}
