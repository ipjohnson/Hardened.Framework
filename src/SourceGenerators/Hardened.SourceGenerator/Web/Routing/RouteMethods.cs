namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// Which HTTP methods a leaf answers, beyond the ones its routes declared.
///
/// <para>
/// HEAD is GET without a body. RFC 9110 defines it that way, and everything that probes a URL
/// without wanting its content issues one — health checkers, link validators, CDNs and proxies.
/// A leaf that switched only on the methods its routes declared answered no match for HEAD, so
/// every Hardened endpoint 404'd a <c>curl -I</c> even though the resource was right there.
/// </para>
///
/// <para>
/// The routing side of that is a fall-through case: HEAD reaches the GET handler and runs it
/// unchanged, so the status, content type and every header come out identical to the GET. The
/// body is discarded on the way out rather than never produced, which is what keeps the headers
/// honest — see <c>HeadRequest</c> in Hardened.Requests.Runtime.
/// </para>
/// </summary>
public static class RouteMethods {
    public const string Get = "GET";

    public const string Head = "HEAD";

    /// <summary>
    /// Whether <paramref name="leaf"/> should also answer HEAD.
    ///
    /// <para>
    /// Only GET leaves, and only where nothing at this position declared HEAD itself. No verb
    /// attribute produces a HEAD route today — <c>WebRequestHandlerModelGenerator</c> recognises
    /// Get, Put, Post, Patch and Delete — so the second condition guards a route the generator
    /// cannot yet produce. It is here because the failure it prevents is not a wrong route but
    /// output that does not compile: two <c>case "HEAD":</c> labels in one switch. Adding the
    /// attribute should not also require remembering this.
    /// </para>
    /// </summary>
    public static bool AddsHeadFallThrough<T>(
        IReadOnlyList<RouteTreeLeafNode<T>> siblingLeaves, RouteTreeLeafNode<T> leaf) {
        if (leaf.Method != Get) {
            return false;
        }

        foreach (var sibling in siblingLeaves) {
            if (sibling.Method == Head) {
                return false;
            }
        }

        return true;
    }
}
