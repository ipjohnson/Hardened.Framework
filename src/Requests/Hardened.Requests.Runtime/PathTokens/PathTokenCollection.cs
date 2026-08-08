using Hardened.Requests.Abstract.PathTokens;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Runtime.PathTokens;

/// <summary>
/// Path token values for a matched route.
///
/// Names and values are stored separately. The names belong to the route that matched and
/// are known at compile time, so generated code passes a static array that is shared across
/// every request. Only the values are per-request.
///
/// That separation is what makes overlapping routes work. The route tree shares a node when
/// two routes have a token in the same position, so the node cannot know which name applies
/// - /users/{id} and /users/{userId}/posts/{postId} share their first token position. The
/// matched route's leaf supplies the names, and the values are filled in positionally as the
/// match unwinds.
/// </summary>
public class PathTokenCollection : IPathTokenCollection {
    private readonly string[] _names;
    private readonly string?[] _values;

    /// <summary>
    /// True when _names was allocated here rather than supplied by the caller, and may
    /// therefore be written to. A route-supplied array is shared and must never be mutated.
    /// </summary>
    private readonly bool _ownsNames;

    public static readonly PathTokenCollection Empty = new(0);

    /// <summary>
    /// Names come from the matched route and are expected to be a static, shared array.
    /// </summary>
    public PathTokenCollection(int count, string[] names, string? lastValue = null) {
        _values = new string?[count];
        _names = names;
        _ownsNames = false;

        if (lastValue != null && count > 0) {
            _values[count - 1] = lastValue;
        }
    }

    /// <summary>
    /// Retained for generated code produced before names moved to the route. Such code
    /// supplies a name with every value, so this allocates a names array to write into.
    /// </summary>
    public PathTokenCollection(int count, PathToken? lastToken = null) {
        _values = new string?[count];
        _names = count == 0 ? Array.Empty<string>() : new string[count];
        _ownsNames = true;

        if (lastToken != null && count > 0) {
            _names[count - 1] = lastToken.TokenName;
            _values[count - 1] = lastToken.TokenValue;
        }
    }

    public int Count => _values.Length;

    /// <summary>Sets a value positionally; the name comes from the matched route.</summary>
    public void SetValue(int index, string value) {
        GuardIndex(index);

        _values[index] = value;
    }

    /// <summary>
    /// Retained for generated code that supplies names alongside values. The name is only
    /// recorded when this collection owns its names array - a route-supplied array is shared
    /// across requests and its names already describe the matched route.
    /// </summary>
    public void Set(int index, PathToken pathToken) {
        GuardIndex(index);

        if (_ownsNames) {
            _names[index] = pathToken.TokenName;
        }

        _values[index] = pathToken.TokenValue;
    }

    public PathToken Get(int index) {
        GuardIndex(index);

        return new PathToken(NameAt(index), _values[index] ?? "");
    }

    public StringValues Get(string id) {
        for (var i = 0; i < _values.Length; i++) {
            if (NameAt(i) == id) {
                return _values[i] ?? StringValues.Empty;
            }
        }

        return StringValues.Empty;
    }

    private string NameAt(int index) =>
        index < _names.Length ? _names[index] ?? "" : "";

    private void GuardIndex(int index) {
        if (index < 0 || index >= _values.Length) {
            throw new IndexOutOfRangeException(
                $"Index {index} is outside the expected path token length {_values.Length}");
        }
    }
}
