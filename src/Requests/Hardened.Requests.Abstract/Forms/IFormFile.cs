namespace Hardened.Requests.Abstract.Forms;

/// <summary>
/// A file part of a <c>multipart/form-data</c> body.
/// </summary>
/// <remarks>
/// <para>
/// Bound with <c>[FromForm]</c>, by the part's field name. The whole form is read before the
/// handler runs, so the bytes are already in memory and <see cref="OpenReadStream"/> can be called
/// more than once.
/// </para>
/// <para>
/// Valid only while the request is. The bytes are returned to a pool when the request ends, so a
/// stream opened here must not be kept past the response.
/// </para>
/// </remarks>
public interface IFormFile
{
    /// <summary>The field name the part was sent under.</summary>
    string Name { get; }

    /// <summary>The file name the client sent, from the part's <c>filename</c> parameter.</summary>
    string FileName { get; }

    /// <summary>The part's own <c>Content-Type</c>, or <c>text/plain</c> when it sent none.</summary>
    string ContentType { get; }

    /// <summary>The number of bytes in the file.</summary>
    long Length { get; }

    /// <summary>The file's bytes, from the start.</summary>
    Stream OpenReadStream();
}
