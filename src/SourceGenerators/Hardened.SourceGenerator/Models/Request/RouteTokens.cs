namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// How much of a path a route token matches, and what it may contain.
///
/// <para>
/// <c>{name}</c> matches one segment. <c>{*name}</c> matches the rest of the path, separators
/// included, and may only be the last token in a route. <c>{name:int}</c> matches one segment that
/// passes a constraint.
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

    public const char ConstraintMarker = ':';

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
    /// The name the token binds to, without the marker or the constraint. <c>{*path}</c> binds to a
    /// parameter called <c>path</c> — the asterisk says how much to match, not what to call it —
    /// and <c>{id:int}</c> binds to <c>id</c>.
    /// </summary>
    public static string Name(string token) {
        var start = IsCatchAll(token) ? 1 : 0;
        var end = token.IndexOf(ConstraintMarker, start);

        return end < 0 ? token.Substring(start) : token.Substring(start, end - start);
    }

    /// <summary>
    /// The constraint the token declares, or null. <c>{id:int}</c> is <c>int</c>.
    /// </summary>
    /// <remarks>
    /// Lower-cased, because a constraint name is a keyword in the template rather than an
    /// identifier from the application, and <c>{id:Int}</c> meaning nothing would be a strange
    /// thing to have to discover.
    /// </remarks>
    public static string? Constraint(string token) {
        var marker = token.IndexOf(ConstraintMarker);

        return marker < 0 || marker == token.Length - 1
            ? null
            : token.Substring(marker + 1).ToLowerInvariant();
    }

    /// <summary>
    /// The constraint declared at <paramref name="depth"/>, or null. Depth is 1-based, on the same
    /// terms as <see cref="IsCatchAll(IReadOnlyList{string}, int)"/>.
    /// </summary>
    public static string? Constraint(IReadOnlyList<string> tokens, int depth) {
        var index = depth - 1;

        return index >= 0 && index < tokens.Count ? Constraint(tokens[index]) : null;
    }

    /// <summary>
    /// The names every well-formed token in <paramref name="pathTemplate"/> binds to, in order.
    /// </summary>
    /// <remarks>
    /// An unclosed brace ends the walk, the way <see cref="BindsParameter"/> stops looking at one.
    /// <c>RouteTokenSyntax</c> is what reports it; this only answers what does bind.
    /// </remarks>
    public static IReadOnlyList<string> Names(string pathTemplate) {
        List<string>? names = null;

        var open = pathTemplate.IndexOf('{');

        while (open >= 0) {
            var close = pathTemplate.IndexOf('}', open);

            if (close < 0) {
                break;
            }

            var name = Name(pathTemplate.Substring(open + 1, close - open - 1));

            if (name.Length > 0) {
                (names ??= new List<string>()).Add(name);
            }

            open = pathTemplate.IndexOf('{', close + 1);
        }

        return (IReadOnlyList<string>?)names ?? Array.Empty<string>();
    }

    /// <summary>
    /// Whether <paramref name="pathTemplate"/> declares a token that binds to
    /// <paramref name="parameterName"/>.
    /// </summary>
    /// <remarks>
    /// This is how a handler's parameter is decided to come from the path rather than the body, and
    /// it has to read the token the way the matcher does. A plain <c>Contains("{name}")</c> - which
    /// is what it was - misses <c>{name:int}</c> and <c>{*name}</c> alike, so a constrained or
    /// catch-all token bound from the request body instead: a 500 on a GET with no body, from a
    /// route that matched perfectly.
    /// </remarks>
    public static bool BindsParameter(string pathTemplate, string parameterName) {
        var open = pathTemplate.IndexOf('{');

        while (open >= 0) {
            var close = pathTemplate.IndexOf('}', open);

            if (close < 0) {
                return false;
            }

            if (string.Equals(
                    Name(pathTemplate.Substring(open + 1, close - open - 1)),
                    parameterName,
                    StringComparison.Ordinal)) {
                return true;
            }

            open = pathTemplate.IndexOf('{', close + 1);
        }

        return false;
    }
}
