namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A response body in the shape RFC 9457 defines: a type URI, a title, the status and a detail.
/// </summary>
/// <remarks>
/// <para>
/// A JSON serializer writes a body implementing this as <c>application/problem+json</c>, the media
/// type RFC 9457 registers for it, when it answers a status of 400 or above. The generated document
/// publishes that media type for every status whose declared body implements this. Every problem
/// record Hardened ships implements it, and a record of an application's own can as well.
/// </para>
/// <para>
/// Members beyond these are RFC 9457 extension members, such as the <c>resource</c> a
/// <c>NotFound</c> names.
/// </para>
/// </remarks>
public interface IProblemDetails : IHttpStatusResponse
{
    /// <summary>The URI identifying the kind of problem.</summary>
    string Type { get; }

    /// <summary>A short summary of the kind of problem, the same for every occurrence of it.</summary>
    string Title { get; }

    /// <summary>What went wrong in this occurrence, or null.</summary>
    string? Detail { get; }
}
