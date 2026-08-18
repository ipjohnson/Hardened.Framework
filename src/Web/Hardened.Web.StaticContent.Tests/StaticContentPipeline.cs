using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// A source, a controller and a writer composed the way the mount provider composes them.
/// </summary>
/// <remarks>
/// <para>
/// The pieces are the production ones - <see cref="FileSystemContentSource"/> resolves,
/// <see cref="StaticContentController"/> serves, <see cref="StaticContentWriter"/> writes. What is
/// mimicked here is the two lines of <c>StaticContentMountProvider.GetExecutionRequestHandler</c>
/// that decide whether the mount answers at all, so a test can ask "was this served" without
/// standing up a container and a filter chain for every assertion about a header.
/// </para>
/// <para>
/// The chain itself - which is what carries authorization, HEAD and the 405 - is exercised against
/// the real provider in <c>StaticContentMountProviderTests</c>. Splitting it this way keeps the
/// header contract tests, of which there are many, from each paying for a service provider.
/// </para>
/// </remarks>
public sealed class StaticContentPipeline {
    private readonly IStaticContentSource _source;
    private readonly StaticContentController _controller;
    private readonly IStaticContentConfiguration _configuration;
    private readonly string? _cacheControl;

    public StaticContentPipeline(
        IStaticContentSource source, IStaticContentConfiguration configuration) {
        _source = source;
        _configuration = configuration;
        _controller = new StaticContentController(new MemoryStreamPool());
        _cacheControl = StaticContentWriter.CacheControlFor(configuration);
    }

    /// <returns>
    /// False when nothing here answers the path, which is the provider declining so that something
    /// else - a trailing-slash alternative, a 405, the 404 handler - gets its turn.
    /// </returns>
    public async Task<bool> Handle(IExecutionContext context) {
        if (!_source.Enabled) {
            return false;
        }

        if (_source.Locate(context.Request.Path) == null) {
            return false;
        }

        await _controller.Serve(context, _source, _configuration, _cacheControl);

        return true;
    }
}
