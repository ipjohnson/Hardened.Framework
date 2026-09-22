using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Forms;

/// <summary>
/// The fields and files of an <c>application/x-www-form-urlencoded</c> or
/// <c>multipart/form-data</c> request body.
/// </summary>
/// <remarks>
/// <para>
/// A url-encoded field has the same shape as a query value, because it is the same wire format read
/// from a different place - <c>name=value&amp;name=value</c>, percent-decoded. The one difference is
/// that a form encodes a space as <c>+</c> and a query string does not, which is handled by the
/// parser rather than by callers. A multipart text part is a field too; a part with a
/// <c>filename</c> is a file.
/// </para>
/// <para>
/// <b>Never null.</b> A request with no form body - the wrong content type, an empty body, a GET -
/// reads as an empty collection rather than as an absent one, so a caller never branches on whether
/// a form was sent before asking what was in it.
/// </para>
/// <para>
/// The members after <see cref="Get"/> have defaults, so an implementation written before files
/// existed still compiles. Those defaults report no keys and no files.
/// </para>
/// </remarks>
public interface IFormCollection
{
    /// <summary>How many distinct field names the body carried.</summary>
    int Count { get; }

    /// <summary>
    /// The value for <paramref name="key"/>, or <see cref="StringValues.Empty"/> when the form does
    /// not carry it.
    /// </summary>
    StringValues Get(string key);

    /// <summary>Every field name the body carried, files excluded.</summary>
    IEnumerable<string> Keys => Array.Empty<string>();

    /// <summary>The first file sent under <paramref name="name"/>, or null when there is none.</summary>
    IFormFile? GetFile(string name) => null;

    /// <summary>Every file sent under <paramref name="name"/>, in the order they arrived.</summary>
    IReadOnlyList<IFormFile> GetFiles(string name) => Array.Empty<IFormFile>();
}
