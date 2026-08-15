namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Declares a route constraint the application's own routes may use.
///
/// <code>
/// [RouteConstraint("isbn")]
/// public static bool IsIsbn(ReadOnlySpan&lt;char&gt; value) =&gt; …
/// </code>
///
/// <para>
/// and then <c>[Get("/books/{code:isbn}")]</c>. The generator emits a direct static call - no
/// allocation, no reflection, no registry, and nothing to look up per request.
/// </para>
///
/// <para>
/// A build-time contract, because a constraint has to be known at compile time to be compiled in.
/// That is also what makes the failure legible: a name nothing declares is a build error naming the
/// constraint, rather than a route that silently constrains nothing.
/// </para>
///
/// <para>
/// The method must be <c>static</c>, return <c>bool</c>, and take a single
/// <c>ReadOnlySpan&lt;char&gt;</c>. The span is the rule rather than a preference: a constraint runs
/// on every request that reaches the position it guards, including the ones it rejects, so a
/// signature taking a <c>string</c> would allocate to decide that a request does not match.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class RouteConstraintAttribute : Attribute {
    public RouteConstraintAttribute(string name) {
        Name = name;
    }

    /// <summary>The name a route template uses after the colon.</summary>
    public string Name { get; }
}
