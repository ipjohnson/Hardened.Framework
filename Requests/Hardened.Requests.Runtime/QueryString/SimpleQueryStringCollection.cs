using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Runtime.QueryString
{
    public class SimpleQueryStringCollection : IQueryStringCollection
    {
        private readonly IDictionary<string, string> _queryParameters;

        public SimpleQueryStringCollection(IDictionary<string, string> queryParameters)
        {
            _queryParameters = queryParameters;
        }

        public int Count => _queryParameters.Count;

        public StringValues Get(string key)
        {
            if (_queryParameters.TryGetValue(key, out var value))
            {
                return value;
            }

            return StringValues.Empty;
        }
    }
}
