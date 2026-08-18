using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Forms;

/// <summary>
/// The fields of an <c>application/x-www-form-urlencoded</c> request body.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>IQueryStringCollection</c>, because it is the same wire format read from a
/// different place - <c>name=value&amp;name=value</c>, percent-decoded. The one difference is that
/// a form encodes a space as <c>+</c> and a query string does not, which is handled by the parser
/// rather than by callers.
/// </para>
/// <para>
/// <b>Never null.</b> A request with no form body - the wrong content type, an empty body, a GET -
/// reads as an empty collection rather than as an absent one, so a caller never branches on whether
/// a form was sent before asking what was in it.
/// </para>
/// </remarks>
public interface IFormCollection {
    /// <summary>How many distinct field names the body carried.</summary>
    int Count { get; }

    /// <summary>
    /// The value for <paramref name="key"/>, or <see cref="StringValues.Empty"/> when the form does
    /// not carry it.
    /// </summary>
    StringValues Get(string key);
}
