using System.Text;
using Hardened.SourceGenerator.Models.Request;

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
/// <c>{id:int}</c> is compiled now — see <see cref="RouteConstraintFacts"/> — so what is left here
/// is a constraint nobody declared, an optional token, and a default. Each compiles, routes, and
/// silently fails to do the thing it was written to do. A route is a contract with every client of
/// the application, so a declaration that does not mean what it says has to stop the build rather
/// than reach production.
/// </para>
///
/// <para>
/// The template itself is read strictly for the same reason. An unbalanced brace, a token with no
/// name, and one name declared twice each produced a route that compiled and matched nothing the
/// author meant it to match — <c>[Get("/{eventId")]</c> built with zero warnings and answered 400
/// to every request shape.
/// </para>
/// </summary>
public static class RouteTokenSyntax {

    /// <summary>Which unsupported form a token was written in, if any.</summary>
    public enum Form {
        Supported,

        /// <summary><c>{id:isbn}</c> — a constraint name nothing declares.</summary>
        UnknownConstraint,

        /// <summary><c>{id?}</c> — an optional segment.</summary>
        Optional,

        /// <summary><c>{id=5}</c> — a default value.</summary>
        Default,

        /// <summary><c>{id</c>, or a stray <c>}</c> — a brace with no partner.</summary>
        Unbalanced,

        /// <summary><c>{}</c> or <c>{:int}</c> — a token with nothing to bind to.</summary>
        Unnamed,

        /// <summary>The same token name twice in one route.</summary>
        Duplicate
    }

    public readonly struct Finding {
        public Finding(string token, Form form, string name, string? constraint) {
            Token = token;
            Form = form;
            Name = name;
            Constraint = constraint;
        }

        /// <summary>The token as written, braces included — <c>{id:isbn}</c>.</summary>
        public string Token { get; }

        public Form Form { get; }

        /// <summary>The part before the marker — <c>id</c> for <c>{id:isbn}</c>.</summary>
        public string Name { get; }

        /// <summary>The constraint name, when the finding is about one.</summary>
        public string? Constraint { get; }
    }

    /// <summary>
    /// Every unsupported token in <paramref name="pathTemplate"/>, in the order they appear.
    /// </summary>
    /// <param name="declaredConstraints">
    /// Constraint names the application declares with <c>[RouteConstraint]</c>, beyond the built-in
    /// ones. A generator that has not collected them passes none, and every custom constraint then
    /// reads as unknown - so the caller has to pass what it found.
    /// </param>
    /// <remarks>
    /// Returns an empty list for the overwhelmingly common case, so a route that is fine allocates
    /// nothing.
    /// </remarks>
    public static IReadOnlyList<Finding> Scan(
        string pathTemplate, IReadOnlyCollection<string>? declaredConstraints = null) {
        List<Finding>? findings = null;
        HashSet<string>? seen = null;

        var open = pathTemplate.IndexOf('{');
        var stray = pathTemplate.IndexOf('}');

        // A closing brace before any opening one is as unbalanced as the reverse, and reads the
        // same way to whoever typed it.
        if (stray >= 0 && (open < 0 || stray < open)) {
            Add(ref findings, new Finding(
                pathTemplate.Substring(stray, 1), Form.Unbalanced, "", null));
        }

        while (open >= 0) {
            var close = pathTemplate.IndexOf('}', open);

            // A brace that is never closed used to end the scan silently, which is how
            // [Get("/{eventId")] built with zero warnings: the rest of the template became literal
            // text, the route matched nothing anybody sent, and the parameter it was written to
            // bind was read from the request body instead.
            if (close < 0) {
                Add(ref findings, new Finding(
                    pathTemplate.Substring(open), Form.Unbalanced,
                    Name(pathTemplate.Substring(open + 1)), null));

                break;
            }

            var body = pathTemplate.Substring(open + 1, close - open - 1);
            var token = pathTemplate.Substring(open, close - open + 1);
            var name = Name(body);

            // A second '{' inside a token is the same mistake seen from the other end: one of the
            // two braces has no partner.
            if (body.IndexOf('{') >= 0) {
                Add(ref findings, new Finding(token, Form.Unbalanced, name, null));
            }
            else if (name.Length == 0) {
                Add(ref findings, new Finding(token, Form.Unnamed, name, null));
            }
            else if (!(seen ??= new HashSet<string>(StringComparer.Ordinal)).Add(name)) {
                Add(ref findings, new Finding(token, Form.Duplicate, name, null));
            }
            else {
                var form = Classify(body, declaredConstraints);

                if (form != Form.Supported) {
                    Add(ref findings, new Finding(token, form, name, RouteTokens.Constraint(body)));
                }
            }

            open = pathTemplate.IndexOf('{', close + 1);
        }

        return (IReadOnlyList<Finding>?)findings ?? Array.Empty<Finding>();
    }

    private static void Add(ref List<Finding>? findings, Finding finding) {
        findings ??= new List<Finding>();

        findings.Add(finding);
    }

    /// <summary>
    /// What to tell the author. Built here rather than in the message format string so the
    /// replacement it suggests can carry the token's own name.
    /// </summary>
    public static string Advice(Finding finding) {
        var name = finding.Name.Length > 0 ? finding.Name : "name";

        switch (finding.Form) {
            case Form.UnknownConstraint:
                // A name that exists at another arity is a wrong call, not an unknown name, and
                // saying "nothing declares length" to someone who wrote length(1,2,3) sends them
                // looking for the wrong thing.
                var arity = WrongArity(finding.Constraint);

                if (arity != null) {
                    return arity;
                }

                return
                    $"Nothing declares a route constraint called '{finding.Constraint}'. Built in: " +
                    $"{string.Join(", ", RouteConstraintFacts.Names)}. Declare your own with " +
                    $"[RouteConstraint(\"{finding.Constraint}\")] on a static " +
                    $"bool(ReadOnlySpan<char>) method, or write '{{{name}}}' and let the handler's " +
                    $"parameter type reject a bad value with a 400.";

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

            case Form.Unbalanced:
                return
                    "This brace has no partner, so what was written as a token is matched as " +
                    "literal text and the parameter it was written to bind is read from the " +
                    "request body instead. Close the token.";

            case Form.Unnamed:
                return
                    "A token with no name binds nothing. Name it, or drop the braces if the " +
                    "segment is literal.";

            case Form.Duplicate:
                return
                    $"'{name}' is declared twice in this route. Two tokens of one name cannot both " +
                    $"bind the handler's '{name}' parameter, and only one of the two segments a " +
                    $"request sends would reach it. Give them distinct names.";

            default:
                return "";
        }
    }

    /// <summary>
    /// The message for a real name used with the wrong number of arguments, or null when that is not
    /// what went wrong.
    /// </summary>
    private static string? WrongArity(string? constraint) {
        var terms = constraint == null ? null : RouteConstraintFacts.Terms(constraint);

        if (terms == null) {
            return null;
        }

        foreach (var term in terms) {
            if (RouteConstraintFacts.Call(term) != null) {
                continue;
            }

            var arities = RouteConstraintFacts.Arities(term.Name);

            if (arities.Count == 0) {
                continue;
            }

            var forms = string.Join(
                " or ",
                arities.Select(count => $"'{term.Name}({string.Join(",", Enumerable.Repeat("n", count))})'"));

            return
                $"'{term.Name}' takes {forms}, but was given {term.Arguments.Count} argument" +
                (term.Arguments.Count == 1 ? "" : "s") + ". Arguments are whole numbers.";
        }

        return null;
    }

    /// <summary>
    /// A catch-all marker is not punctuation to reject: <c>{*path}</c> is supported syntax, and
    /// the asterisk is stripped before the name is read. A colon is supported too, as long as
    /// something declares what follows it.
    /// </summary>
    private static Form Classify(string body, IReadOnlyCollection<string>? declaredConstraints) {
        foreach (var character in body) {
            switch (character) {
                case '?':
                    return Form.Optional;

                case '=':
                    return Form.Default;
            }
        }

        var constraint = RouteTokens.Constraint(body);

        if (constraint == null) {
            return Form.Supported;
        }

        var terms = RouteConstraintFacts.Terms(constraint);

        // Not a chain at all - an unclosed paren, an empty argument list, a non-integer argument.
        if (terms == null) {
            return Form.UnknownConstraint;
        }

        foreach (var term in terms) {
            if (RouteConstraintFacts.Call(term) != null) {
                continue;
            }

            // A [RouteConstraint] takes no arguments, so a declared name used with them is not it.
            if (term.Arguments.Count == 0 &&
                declaredConstraints != null &&
                declaredConstraints.Contains(term.Name)) {
                continue;
            }

            return Form.UnknownConstraint;
        }

        return Form.Supported;
    }

    private static string Name(string body) {
        var builder = new StringBuilder(body.Length);

        foreach (var character in body) {
            if (character == RouteTokens.ConstraintMarker || character == '?' || character == '=') {
                break;
            }

            if (character != RouteTokens.CatchAllMarker) {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
