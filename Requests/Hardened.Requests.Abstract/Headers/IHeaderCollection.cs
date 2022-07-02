using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Headers
{
    public interface IHeaderCollection : IEnumerable<KeyValuePair<string, StringValues>>
    {
        bool ContainsKey(string key);

        StringValues Get(String key);

        StringValues Set(String key, StringValues value);

        int Count { get; }

        bool TryGet(string key, out StringValues value);
    }
}
