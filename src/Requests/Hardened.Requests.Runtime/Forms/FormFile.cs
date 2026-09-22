using Hardened.Requests.Abstract.Forms;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// A file part, over the slice of the request's buffer that holds its bytes.
/// </summary>
internal sealed class FormFile : IFormFile
{
    private readonly ArraySegment<byte> _content;

    public FormFile(string name, string fileName, string contentType, ArraySegment<byte> content)
    {
        Name = name;
        FileName = fileName;
        ContentType = contentType;
        _content = content;
    }

    public string Name { get; }

    public string FileName { get; }

    public string ContentType { get; }

    public long Length => _content.Count;

    /// <summary>A read-only stream over the same bytes, not a copy of them.</summary>
    public Stream OpenReadStream() =>
        new MemoryStream(_content.Array!, _content.Offset, _content.Count, writable: false);
}
