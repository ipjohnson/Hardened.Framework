namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The test one route constraint runs against one path segment.
/// </summary>
/// <remarks>
/// <para>
/// A named delegate rather than <c>Func&lt;ReadOnlySpan&lt;char&gt;, bool&gt;</c>, which does not
/// compile: a type argument cannot be a <c>ref struct</c> before .NET 9's
/// <c>allows ref struct</c>, and this package targets net8.0.
/// </para>
/// <para>
/// The span is the rule for the same reason <c>[RouteConstraint]</c> gives: a constraint runs on
/// every request that reaches the position it guards, including the ones it rejects, so a
/// signature taking a string would allocate to decide that a request does not match.
/// </para>
/// </remarks>
public delegate bool RouteConstraintTest(ReadOnlySpan<char> value);
