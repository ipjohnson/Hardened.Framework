using System.Text;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Brace forms borrowed from other routing systems that Hardened does not compile.
///
/// <para>
/// The failure they produce is worse than being ignored: the whole brace body becomes the token
/// <em>name</em>. Verified —
/// </para>
///
/// <code>
/// {id:int} with 7    /constrained/7    -> matched   token names: id:int
/// {id:int} with abc  /constrained/abc  -> matched   token names: id:int
/// {id?}    with 7    /optional/7       -> matched   token names: id?
/// {id?}    omitted   /optional         -> no match  token names: -
/// </code>
///
/// <para>
/// So <c>{id:int}</c> neither constrains the match nor binds a parameter called <c>id</c>, and
/// <c>{id?}</c> is not optional — it is a mandatory segment with a strange name. Both compile,
/// both route, and both silently fail to do the thing they were written to do. A route is a
/// contract with every client of the application, so a declaration that does not mean what it
/// says has to stop the build rather than reach production.
/// </para>
/// </summary>
public static class RouteTokenSyntax {

    /// <summary>Which unsupported form a token was written in, if any.</summary>
    public enum Form {
        Supported,

        /// <summary><c>{id:int}</c> — a type constraint.</summary>
        TypeConstraint,

        /// <summary><c>{id?}</c> — an optional segment.</summary>
        Optional,

        /// <summary><c>{id=5}</c> — a default value.</summary>
        Default
    }

    public readonly struct Finding {
        public Finding(string token, Form form, string name) {
            Token = token;
            Form = form;
            Name = name;
        }

        /// <summary>The token as written, braces included — <c>{id:int}</c>.</summary>
        public string Token { get; }

        public Form Form { get; }

        /// <summary>The part before the unsupported punctuation — <c>id</c> for <c>{id:int}</c>.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// Every unsupported token in <paramref name="pathTemplate"/>, in the order they appear.
    /// Returns an empty list for the overwhelmingly common case, so the caller allocates nothing
    /// on a route that is fine.
    /// </summary>
    public static IReadOnlyList<Finding> Scan(string pathTemplate) {
        List<Finding>? findings = null;

        var open = pathTemplate.IndexOf('{');

        while (open >= 0) {
            var close = pathTemplate.IndexOf('}', open);

            if (close < 0) {
                break;
            }

            var body = pathTemplate.Substring(open + 1, close - open - 1);
            var form = Classify(body);

            if (form != Form.Supported) {
                findings ??= new List<Finding>();

                findings.Add(new Finding(
                    pathTemplate.Substring(open, close - open + 1),
                    form,
                    Name(body)));
            }

            open = pathTemplate.IndexOf('{', close + 1);
        }

        return (IReadOnlyList<Finding>?)findings ?? Array.Empty<Finding>();
    }

    /// <summary>
    /// What to tell the author. Built here rather than in the message format string so the
    /// replacement it suggests can carry the token's own name.
    /// </summary>
    public static string Advice(Finding finding) {
        var name = finding.Name.Length > 0 ? finding.Name : "name";

        switch (finding.Form) {
            case Form.TypeConstraint:
                return
                    $"A type constraint is not compiled - the whole brace body becomes the token " +
                    $"name, so this matches any segment and binds nothing to '{name}'. " +
                    $"Write '{{{name}}}' and let the handler's parameter type reject a bad value.";

            case Form.Optional:
                return
                    $"An optional token is not supported - this is a mandatory segment named " +
                    $"'{finding.Name}', so the path it was written to make optional does not match " +
                    $"at all. Declare the two paths as two routes.";

            case Form.Default:
                return
                    $"A default in the template is not supported - the whole brace body becomes " +
                    $"the token name, so nothing binds to '{name}'. Give the handler's '{name}' " +
                    $"parameter a C# default instead; the template controls matching and C# " +
                    $"supplies the value.";

            default:
                return "";
        }
    }

    /// <summary>
    /// A catch-all marker is not punctuation to reject: <c>{*path}</c> is supported syntax, and
    /// the asterisk is stripped before the name is read.
    /// </summary>
    private static Form Classify(string body) {
        foreach (var character in body) {
            switch (character) {
                case ':':
                    return Form.TypeConstraint;

                case '?':
                    return Form.Optional;

                case '=':
                    return Form.Default;
            }
        }

        return Form.Supported;
    }

    private static string Name(string body) {
        var builder = new StringBuilder(body.Length);

        foreach (var character in body) {
            if (character == ':' || character == '?' || character == '=') {
                break;
            }

            if (character != RouteTokens.CatchAllMarker) {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
