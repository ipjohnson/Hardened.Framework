using System.Globalization;
using Hardened.Requests.Abstract.Errors;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// A <c>multipart/form-data</c> body longer than
/// <see cref="IFormConfiguration.MaxBodyBytes"/> - 413.
/// </summary>
/// <remarks>
/// Thrown from inside the bind, where the body is read, so it reaches the caller the way any other
/// failure to read the body does. The limit is in the message, for the reason
/// <c>DecompressedBodyTooLargeException</c> gives.
/// </remarks>
public class FormBodyTooLargeException : StatusCodeException
{
    public FormBodyTooLargeException(long limit)
        : base(
            413,
            value: null,
            message: "The form body is longer than "
                + limit.ToString(CultureInfo.InvariantCulture)
                + " bytes."
        )
    {
        Limit = limit;
    }

    /// <summary>The cap that was exceeded, in bytes.</summary>
    public long Limit { get; }
}
