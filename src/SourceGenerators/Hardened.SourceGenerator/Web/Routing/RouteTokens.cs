namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// How much of a path a route token matches.
///
/// <para>
/// <c>{name}</c> matches one segment. <c>{*name}</c> matches the rest of the path, separators
/// included, and may only be the last token in a route.
/// </para>
///
/// <para>
/// Every token used to behave like the second form: a token at the end of a route took the whole
/// remainder, and a token in the middle was scanned for by walking the path until the rest of the
/// route matched, so it could span separators too. That made <c>/users/{id}</c> answer
/// <c>/users/42/anything/at/all</c>, and left no way to write a route that matched exactly one
/// segment — which is what almost every route wants, and what makes a 404 possible for a path
/// nobody declared. The asterisk was described in a test comment as the way to ask for the greedy
/// form, but it was only a naming convention: nothing read it, so both forms behaved the same.
/// It is syntax now, and the two forms differ.
/// </para>
/// </summary>
public static class RouteTokens {
    public const char CatchAllMarker = '*';

    public static bool IsCatchAll(string token) =>
        token.Length > 0 && token[0] == CatchAllMarker;

    /// <summary>
    /// Whether the token at <paramref name="depth"/> is a catch-all. Depth is 1-based, matching
    /// <c>RouteTreeNode.WildCardDepth</c>; out-of-range depths are not catch-alls rather than an
    /// error, because a node is shared by routes with different token counts.
    /// </summary>
    public static bool IsCatchAll(IReadOnlyList<string> tokens, int depth) {
        var index = depth - 1;

        return index >= 0 && index < tokens.Count && IsCatchAll(tokens[index]);
    }

    /// <summary>
    /// The name the token binds to, without the marker. <c>{*path}</c> binds to a parameter called
    /// <c>path</c> — the asterisk says how much to match, not what to call it.
    /// </summary>
    public static string Name(string token) =>
        IsCatchAll(token) ? token.Substring(1) : token;
}
