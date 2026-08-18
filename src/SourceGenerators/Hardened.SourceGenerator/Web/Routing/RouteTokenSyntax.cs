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
        Default
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

        var open = pathTemplate.IndexOf('{');

        while (open >= 0) {
            var close = pathTemplate.IndexOf('}', open);

            if (close < 0) {
                break;
            }

            var body = pathTemplate.Substring(open + 1, close - open - 1);
            var form = Classify(body, declaredConstraints);

            if (form != Form.Supported) {
                findings ??= new List<Finding>();

                findings.Add(new Finding(
                    pathTemplate.Substring(open, close - open + 1),
                    form,
                    Name(body),
                    RouteTokens.Constraint(body)));
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
