using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public class ContextHeaderCollection : IHeaderCollection
    {
        private readonly IDictionary<string, string> _customContext;

        public ContextHeaderCollection(IDictionary<string, string> customContext)
        {
            _customContext = customContext;
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            foreach (var kvp in _customContext)
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
            return _customContext.ContainsKey(key);
        }

        public StringValues Get(string key)
        {
            if (_customContext.TryGetValue(key, out var value))
            {
                return value;
            }

            return StringValues.Empty;
        }

        public StringValues Set(string key, object? value)
        {
            throw new NotSupportedException("Collection cannot be modified");
        }

        public StringValues Set(string key, StringValues value)
        {
            throw new NotSupportedException("Collection cannot be modified");
        }

        public int Count => _customContext.Count;

        public bool TryGet(string key, out StringValues value)
        {
            var returnValue = _customContext.TryGetValue(key, out var tempValue);

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
