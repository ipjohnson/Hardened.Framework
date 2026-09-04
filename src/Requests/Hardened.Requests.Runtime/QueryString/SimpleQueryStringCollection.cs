using Hardened.Requests.Abstract.QueryString;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.QueryString;

/// <summary>
/// A parsed query string, keyed by parameter name.
/// </summary>
/// <remarks>
/// Values are <see cref="StringValues"/> because a key may appear more than once -
/// <c>?symbols=EUR&amp;symbols=GBP</c> is how OpenAPI's default array style is written on the wire.
/// The <see cref="IDictionary{TKey,TValue}"/> of strings constructor is the single-valued case,
/// which is most of them and every test fixture.
/// </remarks>
public class SimpleQueryStringCollection : IQueryStringCollection {
    private readonly IDictionary<string, StringValues> _queryParameters;

    public SimpleQueryStringCollection(IDictionary<string, string>? queryParameters) {
        _queryParameters = new Dictionary<string, StringValues>();

        if (queryParameters == null) {
            return;
        }

        foreach (var pair in queryParameters) {
            _queryParameters[pair.Key] = pair.Value;
        }
    }

    public SimpleQueryStringCollection(Dictionary<string, StringValues>? queryParameters) {
        _queryParameters = queryParameters ?? new Dictionary<string, StringValues>();
    }

    public int Count => _queryParameters.Count;

    public StringValues Get(string key) {
        if (_queryParameters.TryGetValue(key, out var value)) {
            return value;
        }

        return StringValues.Empty;
    }
}
