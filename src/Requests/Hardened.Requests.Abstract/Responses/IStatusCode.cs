namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A status code as a type, so a response can be generic in the status it carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> The shipped records cover the statuses worth a type each, and no list
/// of them is ever complete - a description may declare any integer, and 529 is registered
/// nowhere while several services answer with it. Without this the only answer for the tail is a
/// generated type per operation and status, which is the cost the shipped records exist to remove.
/// <see cref="Status{TCode, TBody}"/> closes it: two statuses are two closed types, so a response
/// set holding both clears CS0457 without the framework knowing either number in advance.
/// </para>
/// <para>
/// <b><c>static abstract</c> is what keeps it AOT-safe.</b> <c>TCode</c> is always a struct type
/// argument, so <c>TCode.Status</c> is resolved at compile time and devirtualized - nothing
/// reflects and nothing is looked up per request.
/// </para>
/// <para>
/// A marker also carries <see cref="HttpStatusAttribute"/>, which restates the number for the
/// generator. That is not duplication for its own sake: the generator reads attributes out of
/// metadata and cannot evaluate a property body, which is the same attribute-plus-interface
/// duality <see cref="HttpStatusAttribute"/> already documents for the response types themselves.
/// </para>
/// </remarks>
public interface IStatusCode {

    /// <summary>The status a response marked with this type is written with.</summary>
    static abstract int Status { get; }
}
