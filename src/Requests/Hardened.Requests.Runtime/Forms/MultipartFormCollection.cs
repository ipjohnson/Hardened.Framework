using Hardened.Requests.Abstract.Forms;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// A parsed <c>multipart/form-data</c> body: its text parts as fields, its file parts as files.
/// </summary>
internal sealed class MultipartFormCollection : IFormCollection
{
    private readonly Dictionary<string, StringValues> _fields;
    private readonly Dictionary<string, List<IFormFile>> _files;

    public MultipartFormCollection(
        Dictionary<string, StringValues> fields,
        Dictionary<string, List<IFormFile>> files
    )
    {
        _fields = fields;
        _files = files;
    }

    public int Count => _fields.Count;

    public StringValues Get(string key) =>
        _fields.TryGetValue(key, out var value) ? value : StringValues.Empty;

    public IEnumerable<string> Keys => _fields.Keys;

    public IFormFile? GetFile(string name) =>
        _files.TryGetValue(name, out var files) ? files[0] : null;

    public IReadOnlyList<IFormFile> GetFiles(string name) =>
        _files.TryGetValue(name, out var files) ? files : Array.Empty<IFormFile>();
}
