namespace Hardened.Web.Runtime.Configuration;

/// <summary>
/// What a request whose path differs from a route only by a trailing slash should get.
/// </summary>
/// <remarks>
/// <c>/orders</c> and <c>/orders/</c> are unrelated routes with no option to say otherwise, so any
/// route a client might link either way has to be declared twice. One knob, three settings.
/// </remarks>
public enum TrailingSlash {
    /// <summary>
    /// The two paths are different resources, and only the declared one answers.
    /// </summary>
    /// <remarks>
    /// The default, and what the framework has always done. It is also what an OpenAPI document
    /// says: a path is a path.
    /// </remarks>
    Strict,

    /// <summary>
    /// Both spellings reach the route, and the client sees no difference.
    /// </summary>
    /// <remarks>
    /// One resource answering at two URLs, which is the duplicate-URL problem caches and analytics
    /// then have - but it is what most applications actually want from a link somebody typed.
    /// </remarks>
    Normalise,

    /// <summary>
    /// The other spelling answers <c>308 Permanent Redirect</c> to the declared one.
    /// </summary>
    /// <remarks>
    /// One resource, one URL, and a client that follows it learns which. 308 rather than 301
    /// because a redirect must not change the method: a 301 on a POST is rewritten to GET by most
    /// clients, which silently drops the body.
    /// </remarks>
    Redirect
}

/// <summary>
/// How the web pipeline treats a path that no route matched exactly.
/// </summary>
public interface IWebRoutingConfiguration {
    TrailingSlash TrailingSlash { get; }
}

/// <inheritdoc />
public class WebRoutingConfiguration : IWebRoutingConfiguration {
    public TrailingSlash TrailingSlash { get; set; } = TrailingSlash.Strict;
}
