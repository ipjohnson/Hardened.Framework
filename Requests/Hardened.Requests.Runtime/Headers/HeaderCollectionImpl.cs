using System.Collections;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers
{
    public class HeaderCollectionImpl : IHeaderCollection
    {
        private readonly IDictionary<string,StringValues> _headers;

        public HeaderCollectionImpl() : this(new Dictionary<string, StringValues>())
        {

        }

        public HeaderCollectionImpl(IDictionary<string, StringValues> headers)
        {
            _headers = headers;
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            return _headers.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool ContainsKey(string key)
        {
            return _headers.ContainsKey(key);
        }

        public StringValues Get(string key)
        {
            return _headers[key];
        }

        public StringValues Set(string key, StringValues value)
        {
            return _headers[key] = value;
        }

        public int Count => _headers.Count;

        public bool TryGet(string key, out StringValues value)
        {
            return _headers.TryGetValue(key, out value);
        }
    }
}
