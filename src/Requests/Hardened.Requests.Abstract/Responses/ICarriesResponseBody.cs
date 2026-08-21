namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response type whose body is one of its members rather than the type itself.
/// </summary>
/// <remarks>
/// <para>
/// Most responses <em>are</em> their body: a <c>NotFound</c> serializes as the problem document it
/// describes. A few wrap one instead - <c>Created&lt;T&gt;</c> carries the resource that was made,
/// and <c>NotFound&lt;T&gt;</c> carries a body the caller supplied - and serializing those as
/// themselves would put the payload under a <c>Body</c> or <c>Value</c> member and send the
/// wrapper's own fields with it. A 201 does not look like that anywhere.
/// </para>
/// <para>
/// <b>Read at compile time, not per request.</b> The dispatch generator sees which cases implement
/// this and emits the unwrapping into those arms only, so a handler whose cases are all ordinary
/// responses carries no type test at all. That is the same treatment
/// <see cref="IProvidesResponseHeaders"/> gets, and for the same reason.
/// </para>
/// </remarks>
public interface ICarriesResponseBody {

    /// <summary>
    /// What is serialized for this response, or null to send nothing.
    /// </summary>
    /// <remarks>
    /// Typed <c>object?</c> because the pipeline's <c>ResponseValue</c> is, so nothing is
    /// transformed between here and the wire - the same property that lets a union's <c>Value</c> be
    /// assigned straight across.
    /// </remarks>
    object? Body { get; }
}
