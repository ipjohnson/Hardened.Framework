using System.Text;
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Web.Runtime.Caching;

/// <summary>
/// Keys the response on the route's own tokens, and on nothing else.
///
/// <code>
/// [Get("/pets/{petId}")]
/// [CacheResponse&lt;VaryByRoute&gt;(Duration = 60)]
/// public Pet Get(int petId) =&gt; _pets.Find(petId);
/// </code>
///
/// <para>
/// The strategy for a resource addressed by its URL, and the one worth applying across an
/// application rather than per handler - see
/// <c>GlobalFilterServiceCollectionExtensions.AddGlobalFilter</c>. Every token the route declares
/// is in the key whether or not the handler binds it, because the route is what identifies the
/// resource.
/// </para>
/// </summary>
/// <remarks>
/// The handler's method and path are already in front of every key the filter builds, so a route
/// with no tokens keys on the route alone. That is correct and worth saying out loud: it is a cache
/// of one entry, which is what a collection endpoint taking no parameters should have.
/// </remarks>
public sealed class VaryByRoute : ICacheKeyProvider {

    private static readonly VaryByRoute _instance = new();

    private VaryByRoute() { }

    public static ICacheKeyProvider Create(string[] values) =>
        values.Length == 0
            ? _instance
            : throw new ArgumentException(
                "VaryByRoute keys on the route's own tokens and takes no values, but was given " +
                string.Join(", ", values) + ". Name query keys with VaryByQuery instead.",
                nameof(values));

    public ValueTask<string?> Key(IExecutionContext context) {
        var tokens = context.Request.PathTokens;

        if (tokens.Count == 0) {
            return new ValueTask<string?>(string.Empty);
        }

        var key = new StringBuilder();

        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens.Get(i);

            key.Append(token.TokenName).Append('=').Append(token.TokenValue).Append('&');
        }

        return new ValueTask<string?>(key.ToString());
    }
}
