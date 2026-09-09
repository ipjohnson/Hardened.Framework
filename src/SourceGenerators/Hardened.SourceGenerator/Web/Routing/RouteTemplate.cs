using System.Text;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A Hardened route read as something other than a route: as a document's path template, as a log
/// line an operator reads, and as the constraint on one token.
/// </summary>
/// <remarks>
/// Routing syntax inside a token - the catch-all marker and the constraint chain - is how the
/// router decides what matches. Nothing outside the router wants it: a document that carried it
/// disagreed with its own parameter list, and a warning that carried it named a route the operator
/// cannot find in their own source, because <c>spec_p_588343bc</c> is a hash this build invented.
/// </remarks>
internal static class RouteTemplate {

    /// <summary>
    /// The route with every token reduced to its name.
    ///
    /// <para>
    /// Hardened's own form matches an OpenAPI path template except for the catch-all marker:
    /// <c>/files/{*path}</c> is a Hardened route, and <c>{*path}</c> is not a valid template
    /// expression — an expression is a name, and the name has to match a declared parameter. The
    /// marker says how much of the path the token takes, which is a routing concern the document has
    /// no way to express, so it is dropped and the parameter is written under its own name.
    /// </para>
    ///
    /// <para>
    /// That does lose something: a specification round-tripped back through
    /// <c>Hardened.OpenApi.BuildTask</c> gives a single-segment token where the source route had a
    /// catch-all. Worth knowing, and better than emitting a document no OpenAPI reader accepts.
    /// </para>
    /// </summary>
    public static string NamesOnly(string path) {
        if (path.IndexOf('{') < 0) {
            return path;
        }

        var builder = new StringBuilder(path.Length);
        var index = 0;

        while (index < path.Length) {
            var open = path.IndexOf('{', index);

            if (open < 0) {
                builder.Append(path, index, path.Length - index);
                break;
            }

            var close = path.IndexOf('}', open);

            if (close < 0) {
                builder.Append(path, index, path.Length - index);
                break;
            }

            builder.Append(path, index, open - index).Append('{');

            var start = TokenStart(path, open, close);

            // The constraint: what the token has to look like to match. A template expression is a
            // parameter name and nothing else, so ":int" is not a shorter spelling of a schema - it
            // is a syntax error that happens to parse. Left in, it made the name in the template
            // disagree with the name in "parameters", which Spectral reports as path-params and a
            // generated client turns into a request for /boards/%7BboardId:guid%7D.
            var colon = path.IndexOf(':', start);
            var end = colon >= 0 && colon < close ? colon : close;

            builder.Append(path, start, end - start).Append('}');

            index = close + 1;
        }

        return builder.ToString();
    }

    /// <summary>
    /// The constraint chain written on <paramref name="token"/> - <c>int:min(1)</c> for
    /// <c>{id:int:min(1)}</c> - or an empty string where the token carries none or is not in the
    /// route at all.
    /// </summary>
    /// <remarks>
    /// By token name rather than by position, because the caller has a bound parameter and wants
    /// the constraint guarding it. A parameter that binds from the query or a header is not in the
    /// route, and an empty chain is the truthful answer for it: nothing guards the value.
    /// </remarks>
    public static string ConstraintOn(string path, string token) {
        var open = path.IndexOf('{');

        while (open >= 0) {
            var close = path.IndexOf('}', open);

            if (close < 0) {
                return "";
            }

            var start = TokenStart(path, open, close);
            var colon = path.IndexOf(':', start);
            var nameEnd = colon >= 0 && colon < close ? colon : close;

            if (nameEnd - start == token.Length &&
                string.CompareOrdinal(path, start, token, 0, token.Length) == 0) {
                return nameEnd == close ? "" : path.Substring(colon + 1, close - colon - 1);
            }

            open = path.IndexOf('{', close);
        }

        return "";
    }

    /// <summary>Whether any token in <paramref name="path"/> carries a constraint.</summary>
    /// <remarks>
    /// The question the document writer asks before it says anything about a route constraint at
    /// all, and the reason it can say nothing for the overwhelming majority of routes.
    /// </remarks>
    public static bool HasConstraint(string path) {
        var open = path.IndexOf('{');

        while (open >= 0) {
            var close = path.IndexOf('}', open);

            if (close < 0) {
                return false;
            }

            var colon = path.IndexOf(':', open);

            if (colon > open && colon < close) {
                return true;
            }

            open = path.IndexOf('{', close);
        }

        return false;
    }

    /// <summary>
    /// Where the token's name starts: past the opening brace, and past the catch-all marker where
    /// there is one.
    /// </summary>
    private static int TokenStart(string path, int open, int close) {
        var start = open + 1;

        return start < close && path[start] == '*' ? start + 1 : start;
    }
}
