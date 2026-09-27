using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Caching;

/// <summary>
/// Keys the response on named query-string values, or with no names on every query key the
/// operation binds.
///
/// <code>
/// [Get("/catalog")]
/// [CacheResponse&lt;VaryByQuery&gt;("culture", "region", Duration = 60)]
/// public Catalog Browse(string culture, string region) =&gt; _catalog.For(culture, region);
/// </code>
///
/// <para>
/// Named or declared keys rather than the whole query string. A cache keyed on everything is a
/// cache a caller can miss on at will by adding a parameter nothing reads, which is a request
/// amplifier rather than a cache.
/// </para>
/// <para>
/// With no names, the keys are <see cref="IExecutionRequestHandlerInfo.QueryParameters"/>, which the
/// generator fills from the operation's parameters. A contract's query parameters arrive there the
/// same way, so the key follows the contract rather than a list restated beside it.
/// </para>
/// </summary>
public sealed class VaryByQuery : ICacheKeyProvider
{
    private static readonly VaryByQuery Declared = new(null);

    /// <summary>
    /// The names given, or null to read the operation's own on each request.
    /// </summary>
    private readonly string[]? _keys;

    private VaryByQuery(string[]? keys)
    {
        _keys = keys;
    }

    public static ICacheKeyProvider Create(string[] values) =>
        values.Length > 0 ? new VaryByQuery(values) : Declared;

    public ValueTask<string?> Key(IExecutionContext context)
    {
        var key = new StringBuilder();
        var query = context.Request.QueryString;
        IReadOnlyList<string> names =
            _keys ?? context.HandlerInfo?.QueryParameters ?? Array.Empty<string>();

        // Indexed, because a foreach over the interface allocates an enumerator per request.
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];

            // The name as well as the value. Without it "a=1&b=" and "a=1&b" - or any two keys
            // whose values concatenate the same way - compose one key.
            key.Append(name).Append('=').Append(query.Get(name).ToString()).Append('&');
        }

        return new ValueTask<string?>(key.ToString());
    }
}
