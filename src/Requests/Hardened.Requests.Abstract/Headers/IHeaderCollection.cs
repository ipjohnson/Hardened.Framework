using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers;

public interface IHeaderCollection : IDictionary<string,StringValues> {
    StringValues Append(string key, object value);

    bool ContainsKey(string key);

    StringValues Get(String key);

    StringValues Set(String key, object? value);

    StringValues Set(String key, StringValues value);

    int Count { get; }

    bool TryGet(string key, out StringValues value);

    IDictionary<string, string> ToStringDictionary();
}