namespace Hardened.SourceGenerator.Web.Routing;

public class RouteTreeLeafNode<T> {
    public RouteTreeLeafNode(string method, T value, IReadOnlyList<string> wildCardTokens) {
        Method = method;
        Value = value;
        WildCardTokens = wildCardTokens;
    }

    public string Method { get; }

    public T Value { get; }

    /// <summary>
    /// The token names declared by this route, in order.
    ///
    /// They live here rather than on RouteTreeNode because a node is shared by every route
    /// with a token in that position, and those routes may name it differently -
    /// /users/{id} and /users/{userId}/posts/{postId} share their first token position. Only
    /// the matched route knows what its own tokens are called.
    /// </summary>
    public IReadOnlyList<string> WildCardTokens { get; }
}
