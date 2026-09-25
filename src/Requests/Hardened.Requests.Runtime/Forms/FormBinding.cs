using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// How the generated binder reads the form that a handler's <c>[FromForm]</c> parameters bind from.
/// </summary>
/// <remarks>
/// <para>
/// The 415 is raised here rather than in <see cref="IFormReader.ReadForm"/>, which reads any other
/// content type as an empty form. Code that reads a form itself may rely on that. A handler that
/// declared form parameters cannot use it: a JSON body posted to one bound an empty form, and the
/// caller was told that a field it may well have sent was missing.
/// </para>
/// <para>
/// A request with no <c>Content-Type</c> reads as url-encoded, as it does in the reader.
/// </para>
/// </remarks>
public static class FormBinding
{
    /// <summary>The types a form handler reads, as its 415 lists them.</summary>
    public const string ContentTypes =
        KnownContentType.FormUrlEncoded + ", " + KnownContentType.MultipartFormData;

    /// <summary>The request's form, or a 415 when its body is not a form.</summary>
    /// <exception cref="UnsupportedContentTypeException">
    /// The request carries a <c>Content-Type</c> that is not a form.
    /// </exception>
    public static ValueTask<IFormCollection> Read(IExecutionContext context)
    {
        var contentType = context.Request.ContentType;

        if (!string.IsNullOrEmpty(contentType) && !FormReader.IsFormContentType(contentType))
        {
            throw new UnsupportedContentTypeException(contentType, ContentTypes);
        }

        return context.KnownServices.FormReader.ReadForm(context);
    }
}
