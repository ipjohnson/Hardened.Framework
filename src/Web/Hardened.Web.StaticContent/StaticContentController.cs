using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Serves one located file.
/// </summary>
/// <remarks>
/// <para>
/// A controller rather than the provider writing the response inline, so a static asset has the
/// same shape as any other handler - which is what <c>ExecutionHelper</c> is built to run, and what
/// makes the filter chain, conventions and authorization apply to it unchanged. The same reason
/// <c>OpenApiUiController</c> exists.
/// </para>
/// <para>
/// Stateless, and a singleton for that reason. What varies between two mounts - the source, the
/// configuration, the cache header - is closed over by the lambda the provider hands to
/// <c>ExecutionHelper</c>, not held here.
/// </para>
/// </remarks>
public class StaticContentController {
    private readonly IMemoryStreamPool _memoryStreamPool;

    public StaticContentController(IMemoryStreamPool memoryStreamPool) {
        _memoryStreamPool = memoryStreamPool;
    }

    /// <summary>
    /// Resolves the request against <paramref name="source"/> a second time and writes what it
    /// finds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second time, because routing already asked - and had to, since whether this mount answers
    /// at all is what decides between a handler, a 405 and letting the 404 happen. The repeat is a
    /// dictionary lookup for every request but the first, and the first pays a handful of
    /// <c>File.Exists</c> calls twice. Carrying the location from routing would need somewhere on
    /// the context to put it, and there is nowhere.
    /// </para>
    /// <para>
    /// A 404 here means the file went away between the two - a race, not a mistake. It is answered
    /// rather than declined because by this point the chain has been entered and there is nothing
    /// left to decline to.
    /// </para>
    /// </remarks>
    public async Task Serve(
        IExecutionContext context,
        IStaticContentSource source,
        IStaticContentConfiguration configuration,
        string? cacheControl) {
        var location = source.Locate(context.Request.Path);

        var entry = location == null ? null : await source.Load(location.Value);

        if (entry == null) {
            context.Response.Status = 404;
            context.Response.ShouldSerialize = false;

            return;
        }

        await StaticContentWriter.Write(
            context, entry, configuration, _memoryStreamPool, cacheControl);
    }
}
