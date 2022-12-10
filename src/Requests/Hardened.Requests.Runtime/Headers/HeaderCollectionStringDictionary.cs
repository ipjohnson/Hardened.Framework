using System.Collections;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Utilities;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers;

public class HeaderCollectionStringDictionary : IHeaderCollection
{
    private readonly Dictionary<string, StringValues> _values;

    public HeaderCollectionStringDictionary(IDictionary<string, string> dictionary)
    {
        _values = new Dictionary<string, StringValues>();
        
        foreach (var pair in dictionary)
        {
            _values[pair.Key] = pair.Value.ToStringValues();
        }
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
    {
        return _values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public StringValues Append(string key, object value)
    {
        throw new NotSupportedException("Collection cannot be modified");
    }

    public bool ContainsKey(string key)
    {
        return _values.ContainsKey(key);
    }

    public StringValues Get(string key)
    {
        if (_values.TryGetValue(key, out var value))
        {
            return value;
        }

        return StringValues.Empty;
    }

    public StringValues Set(string key, object? value)
    {
        var stringValue = value?.ToString() ?? string.Empty;
        
        return _values[key] = new StringValues(stringValue.Split(','));
    }

    public StringValues Set(string key, StringValues value)
    {
        return _values[key] = value;
    }

    public int Count => _values.Count;

    public bool TryGet(string key, out StringValues value)
    {
        var returnValue = _values.TryGetValue(key, out var tempValue);

        value = returnValue ? tempValue : StringValues.Empty;

        return returnValue;
    }
}