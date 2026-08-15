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
            _ => null
        };

    /// <summary>Every built-in name, for a diagnostic that has to list them.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "bool", "guid", "int", "long" };
}
