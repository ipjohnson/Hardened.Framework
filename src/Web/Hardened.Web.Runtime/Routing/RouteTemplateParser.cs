namespace Hardened.Web.Runtime.Routing;

/// <summary>What one segment of a route template matches.</summary>
public enum RouteSegmentKind
{
    /// <summary>Text that has to appear as written.</summary>
    Literal,

    /// <summary>One segment, bound to a name.</summary>
    Token,

    /// <summary>The rest of the path, bound to a name.</summary>
    CatchAll,
}

/// <summary>One segment of a parsed route template.</summary>
public readonly struct RouteSegment
{
    public RouteSegment(RouteSegmentKind kind, string value, string? constraint)
    {
        Kind = kind;
        Value = value;
        Constraint = constraint;
    }

    public RouteSegmentKind Kind { get; }

    /// <summary>The literal text, or the name the token binds to.</summary>
    public string Value { get; }

    /// <summary>
    /// The constraint chain as written after the colon - <c>int:min(1)</c> - lower-cased, or null
    /// where the token declares none.
    /// </summary>
    public string? Constraint { get; }
}

/// <summary>
/// Reads a route template that only exists at run time.
/// </summary>
/// <remarks>
/// <para>
/// <b>A second reader of the same grammar, deliberately.</b> The build-time reader is
/// <c>RouteTokenSyntax</c> and <c>RouteConstraintFacts</c> in <c>Hardened.SourceGenerator</c>,
/// which is an analyzer targeting netstandard2.0 and packs its own sources into a package
/// consumers compile. Nothing in it can be linked here without changing what that package ships,
/// and nothing here can be referenced from there. The grammar is small and fixed; the tests in
/// <c>RouteTemplateParserTests</c> pin each form against the one the generator accepts.
/// </para>
/// <para>
/// <b>It reports rather than throws.</b> A registration callback registers many routes at once, and
/// a registry that threw on the first bad one would make a fifty-route registration a fifty-restart
/// debugging session - see <c>RouteRegistrationException</c>, which collects every failure and is
/// thrown once.
/// </para>
/// </remarks>
public static class RouteTemplateParser
{
    private static readonly RouteSegment[] Root = new RouteSegment[0];

    /// <summary>
    /// The segments of <paramref name="template"/>, or the reason it is not a route.
    /// </summary>
    public static bool TryParse(
        string? template,
        out IReadOnlyList<RouteSegment> segments,
        out string? error
    )
    {
        segments = Root;
        error = null;

        if (string.IsNullOrEmpty(template))
        {
            error = "a route template cannot be empty";

            return false;
        }

        if (template![0] != '/')
        {
            error = $"a route template has to start with '/', and '{template}' does not";

            return false;
        }

        // "/" is the root, and splitting it yields one empty piece that is not a segment.
        if (template.Length == 1)
        {
            return true;
        }

        var pieces = template.Substring(1).Split('/');
        var parsed = new RouteSegment[pieces.Length];
        List<string>? names = null;

        for (var index = 0; index < pieces.Length; index++)
        {
            var piece = pieces[index];

            if (!TryParseSegment(piece, template, out parsed[index], out error))
            {
                return false;
            }

            if (parsed[index].Kind == RouteSegmentKind.Literal)
            {
                // An empty literal is a doubled or trailing separator. Trailing is legal and is a
                // route of its own - /orders/ and /orders are unrelated routes, which is what
                // TrailingSlash exists to soften - so only a doubled one is rejected.
                if (piece.Length == 0 && index != pieces.Length - 1)
                {
                    error = $"'{template}' has an empty segment";

                    return false;
                }

                continue;
            }

            names ??= new List<string>();

            if (names.Contains(parsed[index].Value))
            {
                error =
                    $"'{template}' declares the token '{parsed[index].Value}' more than once, so one of them could never be read";

                return false;
            }

            names.Add(parsed[index].Value);

            if (parsed[index].Kind == RouteSegmentKind.CatchAll && index != pieces.Length - 1)
            {
                error =
                    $"'{template}' has the catch-all '{{*{parsed[index].Value}}}' before the end, and a catch-all takes the rest of the path";

                return false;
            }
        }

        segments = parsed;

        return true;
    }

    /// <summary>The token names <paramref name="segments"/> binds, in order.</summary>
    public static string[] TokenNames(IReadOnlyList<RouteSegment> segments)
    {
        var count = 0;

        foreach (var segment in segments)
        {
            if (segment.Kind != RouteSegmentKind.Literal)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new string[count];
        var next = 0;

        foreach (var segment in segments)
        {
            if (segment.Kind != RouteSegmentKind.Literal)
            {
                names[next++] = segment.Value;
            }
        }

        return names;
    }

    private static bool TryParseSegment(
        string piece,
        string template,
        out RouteSegment segment,
        out string? error
    )
    {
        segment = default;
        error = null;

        var open = piece.IndexOf('{');
        var close = piece.IndexOf('}');

        if (open < 0 && close < 0)
        {
            segment = new RouteSegment(RouteSegmentKind.Literal, piece, null);

            return true;
        }

        if (open != 0 || close != piece.Length - 1 || close < open)
        {
            // /v{major} and /{id}.json read as ordinary routes and are not: the compiled table
            // splits a path at its separators, so a token names a whole segment or nothing.
            error =
                $"'{template}' has a token that shares a segment with other text ('{piece}'), and a token names a whole segment";

            return false;
        }

        var body = piece.Substring(1, piece.Length - 2);

        if (body.IndexOf('{') >= 0 || body.IndexOf('}') >= 0)
        {
            error = $"'{template}' has a nested brace in '{piece}'";

            return false;
        }

        var kind = RouteSegmentKind.Token;

        if (body.Length > 0 && body[0] == '*')
        {
            kind = RouteSegmentKind.CatchAll;
            body = body.Substring(1);
        }

        var colon = body.IndexOf(':');
        var name = colon < 0 ? body : body.Substring(0, colon);
        var constraint = colon < 0 ? null : body.Substring(colon + 1).ToLowerInvariant();

        if (name.Length == 0)
        {
            error = $"'{template}' has a token with no name ('{piece}')";

            return false;
        }

        // The forms borrowed from other routing systems that this one does not compile. Left to
        // fall through they would become part of the token's name, which is the failure
        // RouteTokenSyntax exists to stop at build time.
        if (name.IndexOf('?') >= 0)
        {
            error =
                $"'{template}' declares the optional token '{piece}', and a token is not optional here - declare the two routes";

            return false;
        }

        if (name.IndexOf('=') >= 0)
        {
            error =
                $"'{template}' gives the token '{piece}' a default, which nothing reads - declare the two routes";

            return false;
        }

        if (constraint != null && constraint.Length == 0)
        {
            error = $"'{template}' has a token with an empty constraint ('{piece}')";

            return false;
        }

        segment = new RouteSegment(kind, name, constraint);

        return true;
    }
}
