using System.Collections;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.Headers
{
    public class HeaderCollectionStringDictionary : IHeaderCollection
    {
        private readonly IDictionary<string, string> _dictionary;

        public HeaderCollectionStringDictionary(IDictionary<string, string> dictionary)
        {
            _dictionary = dictionary;
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            foreach (var kvp in _dictionary)
            {
                yield return new KeyValuePair<string, StringValues>(kvp.Key, kvp.Value);
            }
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
            return _dictionary.ContainsKey(key);
        }

        public StringValues Get(string key)
        {
            if (_dictionary.TryGetValue(key, out var value))
            {
                return value;
            }

            return StringValues.Empty;
        }

        public StringValues Set(string key, object? value)
        {
            return _dictionary[key] = value?.ToString() ?? string.Empty;
        }

        public StringValues Set(string key, StringValues value)
        {
            _dictionary[key] = value;
            return value;
        }

        public int Count => _dictionary.Count;

        public bool TryGet(string key, out StringValues value)
        {
            var returnValue = _dictionary.TryGetValue(key, out var tempValue);

            if (returnValue)
            {
                value = tempValue;
            }
            else
            {
                value = StringValues.Empty;
            }

            return returnValue;
        }
    }
}

