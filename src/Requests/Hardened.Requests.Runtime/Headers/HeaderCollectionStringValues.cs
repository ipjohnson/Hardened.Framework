using System.Collections;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Utilities;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers;

public class HeaderCollectionStringValues : IHeaderCollection {
    private readonly IDictionary<string, StringValues> _headers;

    public HeaderCollectionStringValues() : this(new Dictionary<string, StringValues>()) { }

    public HeaderCollectionStringValues(IDictionary<string, string>? values) {
        _headers = new Dictionary<string, StringValues>();

        if (values != null) {
            foreach (var pair in values) {
                _headers[pair.Key] = pair.Value.ToStringValues();
            }
        }
    }

    public HeaderCollectionStringValues(IDictionary<string, StringValues> headers) {
        _headers = headers;
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() {
        return _headers.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    public StringValues Append(string key, object value) {
        value ??= "";

        if (TryGet(key, out var stringValues)) {
            stringValues = StringValues.Concat(stringValues, value.ToString());
        }
        else {
            stringValues = value.ToString();
        }

        _headers[key] = stringValues;

        return stringValues;
    }

    public bool ContainsKey(string key) {
        return _headers.ContainsKey(key);
    }

    public StringValues Get(string key) {
        if (_headers.TryGetValue(key, out var stringValues)) {
            return stringValues;
        }

        return StringValues.Empty;
    }

    public StringValues Set(string key, object? value) {
        if (value == null) {
            _headers.Remove(key);

            return StringValues.Empty;
        }

        return Set(key, value.ToString());
    }

    public StringValues Set(string key, StringValues value) {
        return _headers[key] = value;
    }

    public int Count => _headers.Count;

    public bool TryGet(string key, out StringValues value) {
        return _headers.TryGetValue(key, out value);
    }


    public IDictionary<string, string> ToStringDictionary() {
        var dictionary = new Dictionary<string, string>();

        foreach (var pair in _headers) {
            dictionary[pair.Key] = pair.Value.ToString();
        }

        return dictionary;
    }
}