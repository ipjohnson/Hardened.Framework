using System.Globalization;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Which constraint names a route template may use, and what each compiles to.
/// </summary>
/// <remarks>
/// <para>
/// The rule for what belongs here is not "what can we convert" but "what can be tested on a
/// <c>ReadOnlySpan&lt;char&gt;</c> without allocating". A constraint runs on every request that
/// reaches the position it guards, including the ones it rejects, so a test that allocated to
/// decide would make the failure path more expensive than the success path.
/// </para>
/// <para>
/// A name that is not here and is not a <c>[RouteConstraint]</c> declared by the application is a
/// build error. Silently ignoring one would put the route back in the state <c>{id:int}</c> was in
/// before any of this: written, compiled, and constraining nothing.
/// </para>
/// </remarks>
public static class RouteConstraintFacts {
    private const string Runtime = "global::Hardened.Web.Runtime.Routing.RouteConstraints.";

    /// <summary>
    /// The call that tests <paramref name="constraint"/>, or null when nothing built in has that
    /// name.
    /// </summary>
    /// <param name="constraint">The name as written after the colon, already lower-cased.</param>
    /// <returns>
    /// A method group the generated table calls with a span - <c>Is(span)</c> - so the emitter
    /// composes the argument rather than this knowing how the span is spelled.
    /// </returns>
    public static string? Test(string constraint) =>
        constraint switch {
            "int" => Runtime + "IsInt",
            "long" => Runtime + "IsLong",
            "guid" => Runtime + "IsGuid",
            "bool" => Runtime + "IsBool",
            "decimal" => Runtime + "IsDecimal",
            "date" => Runtime + "IsDate",
            "datetime" => Runtime + "IsDateTime",
            "alpha" => Runtime + "IsAlpha",
            "slug" => Runtime + "IsSlug",
            "hex" => Runtime + "IsHex",
            _ => null
        };

    /// <summary>
    /// Where a constraint sorts among alternatives at one token position. Lower is narrower and is
    /// tried first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Declared, not computed.</b> A real subsumption lattice over this vocabulary is mostly
    /// overlap and not worth inferring: a lower-case GUID is a valid <c>slug</c>, <c>hex</c> overlaps
    /// both <c>int</c> and <c>alpha</c>, and <c>123</c> is a perfectly good slug. Inference would
    /// produce an order nobody can predict from the outside, which is the opposite of what a routing
    /// rule needs to be. These numbers are published in the routing guide and are the whole
    /// specification.
    /// </para>
    /// <para>
    /// Gaps are deliberate, so a name can be placed between two existing ones without renumbering
    /// every route table in the world.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The rank, or <see cref="CustomPrecedence"/> for a name no built-in has — which is what a
    /// <c>[RouteConstraint]</c> that does not declare its own precedence gets.
    /// </returns>
    public static int Rank(string constraint) =>
        constraint switch {
            "guid" => 10,
            "date" => 15,
            "datetime" => 15,
            "bool" => 20,
            "int" => 30,
            // min, max and range imply an integer parse, so they are narrower than int alone.
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
            _ => CustomPrecedence
        };

    /// <summary>
    /// Where a <c>[RouteConstraint]</c> sorts when it does not say.
    /// </summary>
    /// <remarks>
    /// After every built-in. A custom constraint is usually narrow, but its author has generally not
    /// thought about ordering, and last-among-constrained is the answer that cannot make an existing
    /// route unreachable.
    /// </remarks>
    public const int CustomPrecedence = 90;

    /// <summary>One constraint in a chain: a name, and the integer arguments it was given.</summary>
    public readonly struct Term {
        public Term(string name, IReadOnlyList<int> arguments) {
            Name = name;
            Arguments = arguments;
        }

        /// <summary>Lower-cased, as it is written after the colon.</summary>
        public string Name { get; }

        public IReadOnlyList<int> Arguments { get; }
    }

    private static readonly int[] NoArguments = new int[0];

    /// <summary>
    /// The terms in a constraint chain — <c>int:min(1)</c> is two — or null when the text is not a
    /// chain at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Arguments are integer literals and nothing else. Every parameterised constraint takes
    /// integers, and admitting strings would bring quoting, escaping and a real parser to a template
    /// grammar that is otherwise a scan.
    /// </para>
    /// <para>
    /// Null is "this does not parse", which the caller reports. It is deliberately distinct from an
    /// empty list, because a token can carry no constraint at all and that is not an error.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Term>? Terms(string chain) {
        if (string.IsNullOrEmpty(chain)) {
            return null;
        }

        var terms = new List<Term>();

        foreach (var part in chain.Split(ChainSeparator)) {
            if (part.Length == 0) {
                return null;
            }

            var open = part.IndexOf('(');

            if (open < 0) {
                // A closing paren with nothing to close is malformed, not a name. Reading it as one
                // would report "nothing declares a constraint called 'length)'", which sends the
                // author looking for a missing declaration rather than a missing bracket.
                if (part.IndexOf(')') >= 0) {
                    return null;
                }

                terms.Add(new Term(part, NoArguments));
                continue;
            }

            if (part[part.Length - 1] != ')' || open == 0) {
                return null;
            }

            var name = part.Substring(0, open);
            var inside = part.Substring(open + 1, part.Length - open - 2);

            if (inside.Length == 0) {
                return null;
            }

            var arguments = new List<int>();

            foreach (var argument in inside.Split(',')) {
                if (!int.TryParse(
                        argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                    return null;
                }

                arguments.Add(parsed);
            }

            terms.Add(new Term(name, arguments));
        }

        return terms;
    }

    private const char ChainSeparator = ':';

    /// <summary>
    /// The call that tests <paramref name="term"/>, or null when nothing built in has that name at
    /// that arity.
    /// </summary>
    /// <remarks>
    /// Arity is part of the lookup rather than checked after it, because <c>length</c> is two
    /// different tests: <c>length(6)</c> is an equality and <c>length(3,9)</c> is a pair of bounds.
    /// A name that exists at the wrong arity has to read as a wrong call, not as an unknown name.
    /// </remarks>
    public static string? Call(Term term) =>
        (term.Name, term.Arguments.Count) switch {
            (_, 0) => Test(term.Name),
            ("length", 1) => Runtime + "IsLength",
            ("length", 2) => Runtime + "IsLength",
            ("minlength", 1) => Runtime + "IsMinLength",
            ("maxlength", 1) => Runtime + "IsMaxLength",
            ("min", 1) => Runtime + "IsMin",
            ("max", 1) => Runtime + "IsMax",
            ("range", 2) => Runtime + "IsRange",
            _ => null
        };

    /// <summary>
    /// The argument counts a parameterised name accepts, for the diagnostic that has to say so.
    /// Empty when the name takes none.
    /// </summary>
    public static IReadOnlyList<int> Arities(string name) =>
        name switch {
            "length" => new[] { 1, 2 },
            "minlength" => new[] { 1 },
            "maxlength" => new[] { 1 },
            "min" => new[] { 1 },
            "max" => new[] { 1 },
            "range" => new[] { 2 },
            _ => new int[0]
        };

    /// <summary>Every built-in name, for a diagnostic that has to list them.</summary>
    /// <remarks>Alphabetical, because this is read by a person in an error message.</remarks>
    public static readonly IReadOnlyList<string> Names = new[] {
        "alpha", "bool", "date", "datetime", "decimal", "guid", "hex", "int", "long", "slug"
    };
}
