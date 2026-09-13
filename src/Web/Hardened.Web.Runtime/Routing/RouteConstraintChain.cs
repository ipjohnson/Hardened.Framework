using System.Globalization;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// Turns the constraint chain written on a token into the tests that guard it.
/// </summary>
/// <remarks>
/// <para>
/// The build-time counterpart is <c>RouteConstraintFacts</c> in <c>Hardened.SourceGenerator</c>,
/// which resolves the same names to a method group the emitted table calls directly. This resolves
/// them to delegates, once, while the table is being built - so a request that reaches a
/// constrained position pays one delegate call and allocates nothing, and a name nothing declares
/// is a registration failure rather than a route that constrains nothing.
/// </para>
/// <para>
/// The names, their arities and their precedence numbers are the ones published in the routing
/// guide. They are duplicated here for the reason <see cref="RouteTemplateParser"/> gives.
/// </para>
/// </remarks>
public static class RouteConstraintChain
{
    private static readonly RouteConstraintTest[] None = new RouteConstraintTest[0];

    /// <summary>
    /// Where a token with no constraint at all sorts among alternatives at one position: after
    /// every constrained one, because it matches everything they do.
    /// </summary>
    public const int UnconstrainedPrecedence = int.MaxValue;

    /// <summary>
    /// Where a <c>[RouteConstraint]</c> the application declared sorts when it does not say.
    /// </summary>
    public const int CustomPrecedence = 90;

    /// <summary>
    /// The tests <paramref name="chain"/> compiles to, and where the token sorts among alternatives
    /// at the same position.
    /// </summary>
    /// <param name="custom">
    /// The <c>[RouteConstraint]</c> methods the application declared, by the name a template uses
    /// after the colon.
    /// </param>
    public static bool TryResolve(
        string? chain,
        IReadOnlyDictionary<string, RouteConstraintTest>? custom,
        out RouteConstraintTest[] tests,
        out int rank,
        out string? error
    )
    {
        tests = None;
        rank = UnconstrainedPrecedence;
        error = null;

        if (string.IsNullOrEmpty(chain))
        {
            return true;
        }

        var terms = chain!.Split(':');
        var resolved = new RouteConstraintTest[terms.Length];

        for (var index = 0; index < terms.Length; index++)
        {
            if (!TryTerm(terms[index], custom, out resolved[index], out var termRank, out error))
            {
                return false;
            }

            if (termRank < rank)
            {
                rank = termRank;
            }
        }

        tests = resolved;

        return true;
    }

    /// <summary>Whether every test passes for <paramref name="value"/>.</summary>
    public static bool Passes(RouteConstraintTest[] tests, ReadOnlySpan<char> value)
    {
        foreach (var test in tests)
        {
            if (!test(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryTerm(
        string term,
        IReadOnlyDictionary<string, RouteConstraintTest>? custom,
        out RouteConstraintTest test,
        out int rank,
        out string? error
    )
    {
        test = null!;
        rank = CustomPrecedence;
        error = null;

        if (term.Length == 0)
        {
            error = "a constraint chain has an empty term";

            return false;
        }

        var open = term.IndexOf('(');

        if (open < 0)
        {
            // A closing paren with nothing to close is malformed, not a name. Read as one it would
            // report "nothing declares a constraint called 'length)'", which sends whoever wrote it
            // looking for a missing declaration rather than a missing bracket.
            if (term.IndexOf(')') >= 0)
            {
                error = $"the constraint '{term}' has a ')' with no '('";

                return false;
            }

            rank = Rank(term);

            var builtIn = Test(term);

            if (builtIn != null)
            {
                test = builtIn;

                return true;
            }

            if (custom != null && custom.TryGetValue(term, out var declared))
            {
                test = declared;
                rank = CustomPrecedence;

                return true;
            }

            error = $"nothing declares a route constraint called '{term}'";

            return false;
        }

        if (term[term.Length - 1] != ')' || open == 0)
        {
            error = $"the constraint '{term}' is not a name followed by its arguments in brackets";

            return false;
        }

        var name = term.Substring(0, open);
        var inside = term.Substring(open + 1, term.Length - open - 2);

        if (inside.Length == 0)
        {
            error = $"the constraint '{term}' has no arguments between its brackets";

            return false;
        }

        var written = inside.Split(',');
        var arguments = new int[written.Length];

        for (var index = 0; index < written.Length; index++)
        {
            if (
                !int.TryParse(
                    written[index],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out arguments[index]
                )
            )
            {
                error =
                    $"the constraint '{term}' takes whole numbers, and '{written[index]}' is not one";

                return false;
            }
        }

        rank = Rank(name);

        // Arity is part of the lookup rather than checked after it, because length is two different
        // tests: length(6) is an equality and length(3,9) is a pair of bounds. A name that exists at
        // the wrong arity has to read as a wrong call, not as an unknown name.
        test = (name, arguments.Length) switch
        {
            ("length", 1) => value => RouteConstraints.IsLength(value, arguments[0]),
            ("length", 2) => value => RouteConstraints.IsLength(value, arguments[0], arguments[1]),
            ("minlength", 1) => value => RouteConstraints.IsMinLength(value, arguments[0]),
            ("maxlength", 1) => value => RouteConstraints.IsMaxLength(value, arguments[0]),
            ("min", 1) => value => RouteConstraints.IsMin(value, arguments[0]),
            ("max", 1) => value => RouteConstraints.IsMax(value, arguments[0]),
            ("range", 2) => value => RouteConstraints.IsRange(value, arguments[0], arguments[1]),
            _ => null!,
        };

        if (test == null)
        {
            error =
                Parameterised(name)
                    ? $"the route constraint '{name}' does not take {arguments.Length} argument(s)"
                : Test(name) != null || custom?.ContainsKey(name) == true
                    ? $"the route constraint '{name}' takes no arguments"
                : $"nothing declares a route constraint called '{name}'";

            return false;
        }

        return true;
    }

    private static RouteConstraintTest? Test(string constraint) =>
        constraint switch
        {
            RouteConstraints.Int => RouteConstraints.IsInt,
            RouteConstraints.Long => RouteConstraints.IsLong,
            RouteConstraints.Guid => RouteConstraints.IsGuid,
            RouteConstraints.Bool => RouteConstraints.IsBool,
            RouteConstraints.Decimal => RouteConstraints.IsDecimal,
            RouteConstraints.Date => RouteConstraints.IsDate,
            RouteConstraints.DateTime => RouteConstraints.IsDateTime,
            RouteConstraints.Alpha => RouteConstraints.IsAlpha,
            RouteConstraints.Slug => RouteConstraints.IsSlug,
            RouteConstraints.Hex => RouteConstraints.IsHex,
            _ => null,
        };

    /// <summary>
    /// Where a constraint sorts among alternatives at one token position. Lower is narrower and is
    /// tried first. The numbers are <c>RouteConstraintFacts.Rank</c>'s.
    /// </summary>
    public static int Rank(string constraint) =>
        constraint switch
        {
            "guid" => 10,
            "date" => 15,
            "datetime" => 15,
            "bool" => 20,
            "int" => 30,
            "min" => 32,
            "max" => 32,
            "range" => 32,
            "long" => 35,
            "decimal" => 40,
            "hex" => 50,
            "alpha" => 60,
            "slug" => 70,
            "length" => 80,
            "minlength" => 80,
            "maxlength" => 80,
            _ => CustomPrecedence,
        };

    /// <summary>
    /// Whether the name takes arguments at some arity, for the message a wrong call gets.
    /// </summary>
    private static bool Parameterised(string name) =>
        name is "length" or "minlength" or "maxlength" or "min" or "max" or "range";
}
