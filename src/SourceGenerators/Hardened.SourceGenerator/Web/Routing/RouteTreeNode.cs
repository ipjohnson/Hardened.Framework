namespace Hardened.SourceGenerator.Web.Routing;

public class RouteTreeNode<T> {
    public RouteTreeNode(
        string path,
        IReadOnlyList<RouteTreeNode<T>> childNodes,
        IReadOnlyList<RouteTreeNode<T>> wildCardNodes,
        IReadOnlyList<RouteTreeLeafNode<T>> leafNodes,
        int wildCardDepth) {
        Path = path == "\0" ? "" : path;
        ChildNodes = childNodes;
        WildCardNodes = wildCardNodes;
        LeafNodes = leafNodes;
        WildCardDepth = wildCardDepth;
    }

    public int WildCardDepth { get; }

    public string Path { get; }

    public string? WildCardToken { get; set; }

    /// <summary>
    /// Whether the token this node scans for is a catch-all — <c>{*name}</c> — and so may span
    /// path separators.
    ///
    /// <para>
    /// Unlike <see cref="RouteTreeLeafNode{T}.WildCardTokens"/>, this has to live on the node: the
    /// scan that finds where a token ends runs before the match unwinds, so it cannot yet know
    /// which route won. Where routes disagree at a shared position, the node is greedy if any of
    /// them declared it so — the permissive reading, because a bounded scan would make the
    /// catch-all route unreachable, whereas an unbounded one only leaves the stricter route
    /// matching more than it asked for. That combination is a route conflict either way.
    /// </para>
    /// </summary>
    public bool WildCardIsCatchAll { get; set; }

    /// <summary>
    /// The constraint the token at this position declares - <c>int</c>, <c>guid</c>, a custom name
    /// - or null when it declares none.
    /// </summary>
    /// <remarks>
    /// On the node for the same reason the catch-all flag is: the test runs while the match is
    /// being made, before it is known which route won. Two routes reaching this node with different
    /// constraints is HRDR001, so by the time this is read they agree.
    /// </remarks>
    public string? WildCardConstraint { get; set; }

    public IReadOnlyList<RouteTreeNode<T>> ChildNodes { get; }

    public IReadOnlyList<RouteTreeNode<T>> WildCardNodes { get; }

    public IReadOnlyList<RouteTreeLeafNode<T>> LeafNodes { get; }
}