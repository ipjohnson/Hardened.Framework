using System.Globalization;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The tests a route token's constraint compiles to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A constraint is a guarantee, not a selector.</b> <c>/users/{id}</c> with an <c>int id</c>
/// answers <c>/users/abc</c> with 400 - the route matched and the binder failed.
/// <c>/users/{id:int}</c> makes it a 404, which is the truthful answer: there is no resource at
/// that URL, and 400 implies you addressed a real endpoint incorrectly. It also rejects garbage
/// before any filter or binder runs.
/// </para>
/// <para>
/// The rule for what belongs here is not "what can we convert" but "what can be tested on a
/// <see cref="ReadOnlySpan{T}"/> without allocating". A constraint runs on every request that
/// reaches the position it guards, including the ones it rejects, so anything that allocates to
/// decide would make the failure path more expensive than the success path.
/// </para>
/// <para>
/// Invariant culture throughout. A route is part of a URL, which is the same string in every
/// locale - parsing <c>{id:int}</c> under a culture with a different negative sign would make the
/// same request match on one machine and not another.
/// </para>
/// </remarks>
public static class RouteConstraints {

    /// <summary>The names a route template may use, and what each compiles to.</summary>
    public const string Int = "int";

    public const string Long = "long";

    public const string Guid = "guid";

    public const string Bool = "bool";

    public static bool IsInt(ReadOnlySpan<char> value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsLong(ReadOnlySpan<char> value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    public static bool IsGuid(ReadOnlySpan<char> value) =>
        System.Guid.TryParse(value, out _);

    public static bool IsBool(ReadOnlySpan<char> value) =>
        bool.TryParse(value, out _);
}
