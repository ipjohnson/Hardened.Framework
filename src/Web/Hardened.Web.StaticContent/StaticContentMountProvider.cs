using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hardened.Web.StaticContent;

/// <summary>
/// Serves a directory of files as routes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its chain is built by <see cref="ExecutionHelper"/>, and that is the whole point of this
/// type.</b> Static content used to be a fall through: <c>WebExecutionHandlerService</c> called a
/// handler directly once routing had failed, so nothing that hangs off a handler reached it - no
/// filters, no conventions, no authorization, no HEAD handling, no 405, no <c>RequestMapped</c>.
/// An application that adopted <c>[RequireAuthorization]</c>, whose entire premise is that an
/// unannotated handler is denied rather than public, still served everything under its content root
/// anonymously and got no diagnostic saying so.
/// </para>
/// <para>
/// Going through the same helper every generated handler goes through fixes all of it at once and
/// adds no authorization code: <c>CreateFilterArray</c> applies conventions and then asks
/// <c>IGlobalFilterRegistry</c>, which is where <c>AuthorizationFilterProvider</c> lives. The same
/// move <c>OpenApiUiProvider</c> made for a page whose path is configuration rather than a
/// compile-time constant, which is exactly this situation.
/// </para>
/// <para>
/// <b>There is deliberately no <c>[AllowAnonymous]</c> in the metadata.</b> That is the one thing a
/// convention cannot narrow. Without it a mount inherits the application's posture: public where no
/// authorization is configured, denied under default-deny, and gate-able by convention everywhere
/// else - three behaviours and nothing to configure. A mount that wants a policy of its own states
/// it as <see cref="IStaticContentConfiguration.Requirement"/>, which
/// <c>IExecutionRequestHandlerInfo</c> documents as the supported way for a handler registered by
/// hand to say what it needs.
/// </para>
/// <para>
/// <b>An <see cref="IFallbackRequestHandlerProvider"/>, so it is consulted after every ordinary
/// provider whatever order the modules were listed in.</b> A directory of files can shadow any path
/// at all, so it has to be asked last - and once it ships in its own package, no amount of care at
/// the registration site can guarantee that, because where this package's registrations land
/// relative to another module's is the application's choice rather than this package's.
/// </para>
/// </remarks>
public class StaticContentMountProvider : IFallbackRequestHandlerProvider {

    /// <summary>What a request to a file may do, when it did something else.</summary>
    private const string Allow = "GET, HEAD";

    private readonly IServiceProvider _serviceProvider;

    private Mount? _mount;

    /// <remarks>
    /// Takes the container rather than what it needs, and resolves on first request rather than
    /// here. <c>HardenedWebModule.ConfigureServices</c> has to stand on its own - a test and an
    /// application both compose it directly - and the source's own dependencies are registered by
    /// other modules, so a factory that resolved them would fail while the container was still being
    /// built, for every application, including one serving no static content at all.
    /// </remarks>
    public StaticContentMountProvider(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        var mount = _mount ??= Mount.Resolve(_serviceProvider);

        if (!mount.Source.Enabled) {
            return null;
        }

        var location = mount.Source.Locate(context.Request.Path);

        if (location == null) {
            return null;
        }

        var method = context.Request.Method;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            // A file that exists answers 405 for a verb it does not: the resource is there and the
            // verb is the problem, which is a distinction a client and a CDN both read. A path that
            // only resolved because a single-page application catches everything is not a resource,
            // so it declines and the 404 happens - answering 405 there would tell a client that
            // POST /api/typo reached something.
            return location.Value.IsFallback ? null : RequestHandlerInfo.MethodNotAllowed(Allow);
        }

        // HEAD reaches the same handler and runs it in full; WebExecutionHandlerService.Dispatch
        // drops the body on the way out and reports the length it would have had. Static content
        // never got that, because Dispatch is only reached by a matched handler.
        return new RequestHandlerInfo(mount.Handler, PathTokenCollection.Empty);
    }

    /// <summary>
    /// One mount's source, configuration and handler, resolved together on first use.
    /// </summary>
    private sealed class Mount {
        private Mount(
            IStaticContentSource source, IStaticContentConfiguration configuration,
            IServiceProvider serviceProvider) {
            Source = source;

            // Built here rather than per request: conventions are asked as a handler is
            // constructed, which is the contract they are written against.
            Handler = new MountHandler(
                source, configuration,
                StaticContentWriter.CacheControlFor(configuration), serviceProvider);
        }

        public IStaticContentSource Source { get; }

        public IExecutionRequestHandler Handler { get; }

        public static Mount Resolve(IServiceProvider serviceProvider) =>
            new(serviceProvider.GetRequiredService<IStaticContentSource>(),
                serviceProvider.GetRequiredService<IOptions<IStaticContentConfiguration>>().Value,
                serviceProvider);
    }

    private sealed class MountHandler : BaseExecutionHandler<StaticContentController> {

        /// <summary>
        /// Empty, and load bearing. See the note on <see cref="StaticContentMountProvider"/>: what
        /// is absent here is what lets a mount inherit the application's authorization posture.
        /// </summary>
        private static readonly object[] Metadata = [];

        public MountHandler(
            IStaticContentSource source,
            IStaticContentConfiguration configuration,
            string? cacheControl,
            IServiceProvider serviceProvider)
            : base(ExecutionHelper.AsyncStandardFilterEmptyParameters<StaticContentController>(
                serviceProvider,
                new ExecutionRequestHandlerInfo(
                    // Not a route anything matched against - the provider decides what this mount
                    // answers - so this names the mount for a log line and a metric rather than
                    // describing a path.
                    "/*", "GET", typeof(StaticContentController),
                    nameof(StaticContentController.Serve), [], Metadata,
                    configuration.Requirement),
                // A lambda rather than a static method, because what varies between two mounts is
                // exactly what it closes over.
                (context, controller) =>
                    controller.Serve(context, source, configuration, cacheControl),
                ExecutionHelper.GetFilterInfo(Metadata))) { }
    }
}
