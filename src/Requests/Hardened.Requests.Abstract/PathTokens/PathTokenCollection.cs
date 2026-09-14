using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.PathTokens;

/// <summary>
/// Path token values for a matched route.
/// </summary>
/// <remarks>
/// <para>
/// Names and values are stored separately. The names belong to the route that matched and are known
/// at compile time, so generated code passes a static array that is shared across every request.
/// Only the values are per request.
/// </para>
/// <para>
/// That separation is what makes overlapping routes work. The route tree shares a node when two
/// routes have a token in the same position, so the node cannot know which name applies -
/// <c>/users/{id}</c> and <c>/users/{userId}/posts/{postId}</c> share their first token position.
/// The matched route's leaf supplies the names, and the values are filled in positionally as the
/// match unwinds.
/// </para>
/// <para>
/// <b>Two fields, in the request rather than beside it.</b> This used to be a class holding a
/// <c>string?[]</c>, which is two allocations for every route with a token in it - 72 of the 144
/// bytes a one-token match cost. The request already owns storage this can live in, so
/// <see cref="Execution.IExecutionRequest.PathTokens"/> holds one by value and the generated table
/// writes into it through a <c>ref</c> parameter as the match unwinds. It is two references wide
/// because every request object carries one whether its route has a token or not, and the width is
/// paid there.
/// </para>
/// <para>
/// <b><c>default</c> is the empty collection</b>, and has to be: a request that has not been routed
/// has one, and a handler reads <c>PathTokens</c> without asking whether there are any.
/// </para>
/// </remarks>
public struct PathTokenCollection : IPathTokenCollection
{
    private readonly string[]? _names;

    /// <summary>
    /// Null while nothing is bound, the value itself for a route with one token, and a
    /// <c>string?[]</c> for a route with more.
    /// </summary>
    /// <remarks>
    /// One token is the overwhelming majority of routes that have any, and holding its value here
    /// is what makes that case allocate nothing but the value string. An array for it would cost 32
    /// bytes to hold one reference.
    /// </remarks>
    private object? _values;

    /// <summary>A collection with no tokens in it, which is what <c>default</c> already is.</summary>
    public static readonly PathTokenCollection Empty = default;

    /// <param name="names">
    /// The matched route's token names, expected to be a static array shared across requests. Never
    /// written to, and its length is the token count: a route binds exactly the tokens it declares.
    /// </param>
    /// <param name="lastValue">
    /// The value of the last token, which the leaf of a route ending in one already has in hand.
    /// </param>
    public PathTokenCollection(string[] names, string? lastValue = null)
    {
        _names = names;
        _values = names.Length > 1 ? new string?[names.Length] : null;

        if (lastValue != null && names.Length > 0)
        {
            SetValue(names.Length - 1, lastValue);
        }
    }

    public readonly int Count => _names?.Length ?? 0;

    /// <summary>Sets a value positionally; the name comes from the matched route.</summary>
    public void SetValue(int index, string value)
    {
        GuardIndex(index);

        if (_values is string?[] values)
        {
            values[index] = value;

            return;
        }

        // One token, so the guard above has already established that index is 0.
        _values = value;
    }

    public readonly PathToken Get(int index)
    {
        GuardIndex(index);

        return new PathToken(_names![index] ?? "", ValueAt(index) ?? "");
    }

    public readonly StringValues Get(string id)
    {
        var names = _names;

        if (names == null)
        {
            return StringValues.Empty;
        }

        for (var i = 0; i < names.Length; i++)
        {
            if (names[i] == id)
            {
                return ValueAt(i) ?? StringValues.Empty;
            }
        }

        return StringValues.Empty;
    }

    private readonly string? ValueAt(int index) =>
        _values is string?[] values ? values[index] : (string?)_values;

    private readonly void GuardIndex(int index)
    {
        if (index < 0 || index >= Count)
        {
            throw new IndexOutOfRangeException(
                $"Index {index} is outside the expected path token length {Count}"
            );
        }
    }
}
