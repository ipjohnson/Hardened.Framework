namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response type that states its own status, and whether it has a body at all.
/// </summary>
/// <remarks>
/// <para>
/// The run-time half of <see cref="HttpStatusAttribute"/>. The pipeline needs a status from a value
/// it is holding, and reading it off an attribute means reflecting over a type in an application
/// that may have been trimmed - so the built-in types answer through an interface instead. The
/// attribute stays the declarative form, for the generator and for a user's own type that should
/// not have to implement anything.
/// </para>
/// <para>
/// <see cref="HasBody"/> is here rather than inferred from the status. 204 is bodyless by
/// definition, but 202 is bodyless by choice - it may carry a representation of the accepted work or
/// nothing but a <c>Location</c> - and the difference is the type's to state rather than a table's
/// to guess. It becomes <c>ShouldSerialize</c> on the execution response.
/// </para>
/// </remarks>
public interface IHttpStatusResponse {

    /// <summary>The status this response is written with.</summary>
    int Status { get; }

    /// <summary>
    /// Whether anything is serialized for this response. Most responses have a body; the default
    /// says so, and the two that do not override it.
    /// </summary>
    bool HasBody => true;
}
