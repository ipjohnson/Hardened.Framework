using Hardened.Requests.Abstract.Forms;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// What a request with no form body reads as.
/// </summary>
/// <remarks>
/// A singleton rather than a fresh empty dictionary per request. Most requests carry no form, and
/// the reader returns this for every one of them - a GET, a JSON body, an empty body. Allocating a
/// collection to represent nothing would put that cost on the common path.
/// </remarks>
public class EmptyFormCollection : IFormCollection {
    public static readonly EmptyFormCollection Instance = new();

    private EmptyFormCollection() { }

    public int Count => 0;

    public StringValues Get(string key) => StringValues.Empty;
}

/// <summary>
/// A parsed form, over the dictionary the parser built.
/// </summary>
public class SimpleFormCollection : IFormCollection {
    private readonly IDictionary<string, StringValues> _fields;

    public SimpleFormCollection(IDictionary<string, StringValues> fields) {
        _fields = fields;
    }

    public int Count => _fields.Count;

    public StringValues Get(string key) =>
        _fields.TryGetValue(key, out var value) ? value : StringValues.Empty;
}
