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

    /// <summary>Every built-in name, for a diagnostic that has to list them.</summary>
    /// <remarks>Alphabetical, because this is read by a person in an error message.</remarks>
    public static readonly IReadOnlyList<string> Names = new[] {
        "alpha", "bool", "date", "datetime", "decimal", "guid", "hex", "int", "long", "slug"
    };
}
