using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Caching;

/// <summary>
/// Keys the response on named query-string values.
///
/// <code>
/// [Get("/catalog")]
/// [CacheResponse&lt;VaryByQuery&gt;("culture", "region", Duration = 60)]
/// public Catalog Browse(string culture, string region) =&gt; _catalog.For(culture, region);
/// </code>
///
/// <para>
/// Named keys rather than the whole query string. A cache keyed on everything is a cache a caller
/// can miss on at will by adding a parameter nothing reads, which is a request amplifier rather than
/// a cache.
/// </para>
/// </summary>
public sealed class VaryByQuery : ICacheKeyProvider {

    private readonly string[] _keys;

    private VaryByQuery(string[] keys) {
        _keys = keys;
    }

    public static ICacheKeyProvider Create(string[] values) =>
        values.Length > 0
            ? new VaryByQuery(values)
            : throw new ArgumentException(
                "VaryByQuery needs at least one query key to vary on.", nameof(values));

    public ValueTask<string?> Key(IExecutionContext context) {
        var key = new StringBuilder();
        var query = context.Request.QueryString;

        foreach (var name in _keys) {
            // The name as well as the value. Without it "a=1&b=" and "a=1&b" - or any two keys
            // whose values concatenate the same way - compose one key.
            key.Append(name).Append('=').Append(query.Get(name).ToString()).Append('&');
        }

        return new ValueTask<string?>(key.ToString());
    }
}
