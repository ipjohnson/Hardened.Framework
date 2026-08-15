namespace Hardened.Requests.Abstract.Links;

/// <summary>
/// Where the application is actually reachable, as opposed to where it thinks it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>A static builder alone is wrong on this framework's primary host.</b>
/// <c>ApiGatewayV2ExecutionRequest</c> does
/// </para>
/// <code>
/// Path = StripStagePath(request.RawPath, request.RequestContext?.Stage);
/// </code>
/// <para>
/// so the application sees <c>/api/products/42</c> while the client must call
/// <c>/prod/api/products/42</c>. A root-relative link built from the route template alone drops the
/// stage and 404s - and the transport already computes the stage before throwing it away.
/// </para>
/// <para>
/// So a generated links type is instance-based and takes one of these. The static path builder is
/// kept alongside for callers who want the raw route with no context at all.
/// </para>
/// </remarks>
public interface ILinkContext {
    /// <summary>
    /// What the transport prefixes to every path before the application sees it. Empty when the
    /// application is served from the root, which is the ordinary case on Kestrel.
    /// </summary>
    string BasePath { get; }

    /// <summary>The scheme to use for an absolute link, or null when none is known.</summary>
    string? Scheme { get; }

    /// <summary>The authority to use for an absolute link, or null when none is known.</summary>
    string? Host { get; }

    /// <summary>
    /// A root-relative link a client can call: the base path, then the route.
    /// </summary>
    string Resolve(string path);

    /// <summary>
    /// An absolute link, for a <c>Location</c> header or anything that leaves the response body.
    /// </summary>
    /// <remarks>
    /// Falls back to <see cref="Resolve"/> when no scheme or host is known, because a relative
    /// Location header is legal per RFC 9110 and a link built against a guessed host is not merely
    /// wrong but wrong in a way that works locally.
    /// </remarks>
    string Absolute(string path);
}
