using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.QueryString;

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