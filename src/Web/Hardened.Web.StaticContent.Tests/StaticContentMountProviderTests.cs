using System.Security.Cryptography;
using System.Text;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.CacheControl;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Streaming;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.StaticContent.Tests;

/// <summary>
/// Static content as a route rather than a fall through.
///
/// <para>
/// These are the tests that could not exist before. A handler the pipeline called directly had no
/// filter chain, so there was nothing to assert about authorization, no <c>Dispatch</c> to drop a
/// HEAD body, and no way to answer 405 - a request either got bytes or it did not. Everything here
/// goes through a real container and a real chain, because a mock of the chain would prove only
/// that the mock was called.
/// </para>
/// </summary>
public class StaticContentMountProviderTests : IDisposable {

    private readonly string _tempRoot;
    private readonly string _staticRoot;

    public StaticContentMountProviderTests() {
        _tempRoot = Path.Combine(Path.GetTempPath(), "hardened-mount-" + Guid.NewGuid().ToString("N"));
        _staticRoot = Path.Combine(_tempRoot, "wwwroot");

        Directory.CreateDirectory(_staticRoot);

        File.WriteAllText(Path.Combine(_staticRoot, "app.js"), "console.log('hi');");
    }

    public void Dispose() {
        try { Directory.Delete(_tempRoot, true); } catch { /* best effort */ }

        GC.SuppressFinalize(this);
    }

    #region harness

    /// <summary>
    /// An application composed the way one really is, with the content root pointed at the fixture.
    /// </summary>
    /// <param name="requireAuthorization">
    /// Whether the application declared <c>[RequireAuthorization]</c>. The attribute is compile-time,
    /// so what it turns on at run time - the filter provider with its backstop enabled - is installed
    /// here directly, which is exactly what <c>AuthorizationStartupService</c> does from it.
    /// </param>
    private ServiceProvider Application(
        bool requireAuthorization = false, Requirement? mountRequirement = null) =>
        Application(configuration => configuration.Requirement.Returns(mountRequirement),
            requireAuthorization);

    private ServiceProvider Application(
        Action<IStaticContentConfiguration> configure, bool requireAuthorization = false) {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);
        new HardenedStaticContent().ConfigureServices(services);

        // What the generated module registration would supply. Registered by hand here rather than
        // by composing every module, because this test needs a working filter chain and not a
        // working application - and standing up the second to assert about the first is what the
        // integration SUTs are for.
        services.AddLogging();
        services.TryAddSingleton<IGlobalFilterRegistry, GlobalFilterRegistry>();
        services.TryAddSingleton<IIOFilterProvider, IOFilterProvider>();
        services.TryAddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        services.TryAddSingleton<IFileExtToMimeTypeHelper, FileExtToMimeTypeHelper>();
        services.TryAddSingleton<IGZipStaticContentCompressor, GZipStaticContentCompressor>();
        services.TryAddSingleton<IETagProvider, ETagProvider>();
        services.TryAddSingleton<IMemoryStreamPool, MemoryStreamPool>();
        services.TryAddSingleton<IItemPool<SHA256>>(
            _ => new ItemPool<SHA256>(SHA256.Create, _ => { }, hash => hash.Dispose()));

        // Never reached - every response here sets ShouldSerialize false - but IOFilterProvider
        // takes them to construct.
        services.TryAddSingleton(Substitute.For<IContextSerializationService>());
        services.TryAddSingleton(
            Options.Create<IResponseHeaderConfiguration>(new ResponseHeaderConfiguration()));
        services.TryAddSingleton(
            Options.Create<IStreamingConfiguration>(new StreamingConfiguration()));

        var configuration = Substitute.For<IStaticContentConfiguration>();

        configuration.Path.Returns(_staticRoot);
        configuration.CacheContent.Returns(true);
        configuration.CacheControlType.Returns(
            CacheControlEnum.MaxAge | CacheControlEnum.Public);
        configuration.EnableRangeRequests.Returns(true);
        configuration.EnableETag.Returns(true);
        configuration.CompressTextContent.Returns(false);
        configuration.FallBackFile.Returns((string?)null);
        configuration.CacheMaxAge.Returns((int?)null);
        configuration.Requirement.Returns((Requirement?)null);

        configure(configuration);

        services.AddSingleton(Options.Create(configuration));

        var provider = services.BuildServiceProvider();

        // The attribute is compile-time, so what [RequireAuthorization] turns on at run time - the
        // filter provider with its backstop enabled - is installed directly, which is exactly what
        // AuthorizationStartupService does from it.
        provider.GetRequiredService<IGlobalFilterRegistry>()
            .RegisterFilter(new AuthorizationFilterProvider(requireAuthorization).GetFilter);

        return provider;
    }

    private static StaticContentMountProvider Mount(IServiceProvider provider) =>
        provider.GetServices<IFallbackRequestHandlerProvider>()
            .OfType<StaticContentMountProvider>()
            .Single();

    private static (IExecutionContext context, MemoryStream body, IExecutionResponse response,
        IDictionary<string, StringValues> headers)
        Context(IServiceProvider services, string path, string method = "GET") {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        request.Path.Returns(path);
        request.Method.Returns(method);
        request.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

        response.Body.Returns(body);
        response.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));
        response.ShouldSerialize.Returns(true);

        context.Request.Returns(request);
        context.Response.Returns(response);
        context.RequestServices.Returns(services);
        context.RootServiceProvider.Returns(services);
        context.CallerPrincipal.Returns(AnonymousCallerPrincipal.Instance);

        return (context, body, response, headers: response.Headers);
    }

    /// <summary>Runs the mount's chain for a request, the way <c>Dispatch</c> would.</summary>
    private static async Task Serve(IServiceProvider services, IExecutionContext context) {
        var match = Mount(services).GetExecutionRequestHandler(context);

        Assert.NotNull(match);
        Assert.NotNull(match.Handler);

        context.HandlerInfo = match.Handler!.HandlerInfo;

        await match.Handler.GetExecutionChain(context).Next();
    }

    private static string Served(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    #endregion

    #region authorization

    /// <summary>
    /// With no authorization configured a file is public, which is what every application serving
    /// static content today expects and must keep getting.
    /// </summary>
    [Fact]
    public async Task AFileIsPublicWhenNoAuthorizationIsConfigured() {
        using var application = Application();

        var (context, body, _, _) = Context(application, "/app.js");

        await Serve(application, context);

        Assert.Equal("console.log('hi');", Served(body));
    }

    /// <summary>
    /// <b>The finding this phase exists for.</b> Under <c>[RequireAuthorization]</c> an unannotated
    /// handler is denied, and static content is now one - so an anonymous request for a file is
    /// refused rather than served. It used to be served, because the pipeline called the static
    /// handler directly and no filter chain, authorization included, ever ran.
    /// </summary>
    [Fact]
    public async Task DefaultDenyRefusesAnAnonymousRequestForAFile() {
        using var application = Application(requireAuthorization: true);

        var (context, body, response, _) = Context(application, "/app.js");

        await Serve(application, context);

        // The refusal travels as the exception the authorization filter records; the serializer
        // turns it into a status. Asserting it here rather than the status is asserting the
        // mechanism rather than a layer this test does not stand up.
        var refusal = Assert.IsType<AuthorizationException>(response.ExceptionValue);

        Assert.Equal(
            AuthorizationChallenge.AuthenticationRequired().Scheme, refusal.Challenge.Scheme);

        Assert.Empty(body.ToArray());
    }

    /// <summary>
    /// A mount that states a requirement is guarded by it whether or not the application turned on
    /// default-deny, which is what makes a directory a place to put authorization.
    /// </summary>
    [Fact]
    public async Task AMountRequirementRefusesACallerWithoutIt() {
        using var application = Application(mountRequirement: Requirement.Grant("files:read"));

        var (context, body, response, _) = Context(application, "/app.js");

        await Serve(application, context);

        Assert.IsType<AuthorizationException>(response.ExceptionValue);
        Assert.Empty(body.ToArray());
    }

    /// <summary>
    /// The control for the two above: the same request, the same chain, nothing refusing. Without
    /// it an empty body proves only that something went wrong somewhere.
    /// </summary>
    [Fact]
    public async Task AFileIsServedWithNoRefusalRecordedWhenNothingGuardsIt() {
        using var application = Application();

        var (context, body, response, _) = Context(application, "/app.js");

        await Serve(application, context);

        Assert.Null(response.ExceptionValue);
        Assert.Equal("console.log('hi');", Served(body));
    }

    /// <summary>
    /// The requirement reaches the handler as first-class data rather than through an attribute,
    /// which is what <c>IExecutionRequestHandlerInfo</c> documents for a handler registered by hand.
    /// </summary>
    [Fact]
    public void AMountRequirementReachesTheHandlerInfo() {
        var requirement = Requirement.Grant("files:read");

        using var application = Application(mountRequirement: requirement);

        var (context, _, _, _) = Context(application, "/app.js");

        var match = Mount(application).GetExecutionRequestHandler(context);

        Assert.Same(requirement, match!.Handler!.HandlerInfo.Requirement);
    }

    /// <summary>
    /// And a mount with nothing declared carries no requirement, so it inherits whatever the
    /// application's posture is rather than asserting one of its own.
    /// </summary>
    [Fact]
    public void AMountWithNothingDeclaredCarriesNoRequirement() {
        using var application = Application();

        var (context, _, _, _) = Context(application, "/app.js");

        var match = Mount(application).GetExecutionRequestHandler(context);

        Assert.Null(match!.Handler!.HandlerInfo.Requirement);
    }

    #endregion

    #region verbs

    /// <summary>
    /// A HEAD reaches the same handler as the GET. <c>Dispatch</c> is what drops the body, and a
    /// handler is what makes <c>Dispatch</c> run at all - static content never got there before.
    /// </summary>
    [Fact]
    public void AHeadRequestIsMatchedToTheSameHandler() {
        using var application = Application();

        var (get, _, _, _) = Context(application, "/app.js");
        var (head, _, _, _) = Context(application, "/app.js", "HEAD");

        var mount = Mount(application);

        Assert.Same(
            mount.GetExecutionRequestHandler(get)!.Handler,
            mount.GetExecutionRequestHandler(head)!.Handler);
    }

    /// <summary>
    /// A verb a file does not answer is a 405 naming what it does, not a 404. The resource exists
    /// and the verb is the problem, and API Gateway and CloudFront cache the two differently.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void AWriteToAFileIsMethodNotAllowed(string method) {
        using var application = Application();

        var (context, _, _, _) = Context(application, "/app.js", method);

        var match = Mount(application).GetExecutionRequestHandler(context);

        Assert.NotNull(match);
        Assert.Null(match.Handler);
        Assert.Equal("GET, HEAD", match.Allow);
    }

    /// <summary>
    /// A path that only resolves because a single-page application catches everything is not a
    /// resource, so a write to it declines and becomes a 404. Answering 405 would tell a client
    /// that <c>POST /api/typo</c> reached something.
    /// </summary>
    [Fact]
    public void AWriteToAPathThatOnlyTheFallbackAnswersDeclines() {
        File.WriteAllText(Path.Combine(_staticRoot, "index.html"), "<html>shell</html>");

        using var application = Application(
            configuration => configuration.FallBackFile.Returns("/index.html"));

        var (write, _, _, _) = Context(application, "/api/typo", "POST");

        Assert.Null(Mount(application).GetExecutionRequestHandler(write));

        // The same path still serves the shell for a GET.
        var (read, _, _, _) = Context(application, "/app/deep/route");

        Assert.NotNull(Mount(application).GetExecutionRequestHandler(read)?.Handler);
    }

    #endregion

    #region matching

    /// <summary>A path with no file behind it declines, so something else answers.</summary>
    [Fact]
    public void APathWithNoFileDeclines() {
        using var application = Application();

        var (context, _, _, _) = Context(application, "/does-not-exist.js");

        Assert.Null(Mount(application).GetExecutionRequestHandler(context));
    }

    /// <summary>
    /// The handler is built once and shared. Conventions are asked as a handler is constructed, so
    /// building one per request would ask them per request and make an authorization decision that
    /// is meant to be settled at startup a per-request cost.
    /// </summary>
    [Fact]
    public void TheHandlerIsBuiltOnce() {
        using var application = Application();

        var mount = Mount(application);

        var (first, _, _, _) = Context(application, "/app.js");
        var (second, _, _, _) = Context(application, "/app.js");

        Assert.Same(
            mount.GetExecutionRequestHandler(first)!.Handler,
            mount.GetExecutionRequestHandler(second)!.Handler);
    }

    /// <summary>
    /// The mount registers as a fallback and not as an ordinary provider, which is what makes it
    /// consulted last regardless of the order an application listed its modules in. Ordering used
    /// to be a property of where the registration sat, which stopped being controllable the moment
    /// this shipped as its own package.
    /// </summary>
    [Fact]
    public void TheMountRegistersAsAFallbackRatherThanAnOrdinaryProvider() {
        using var application = Application();

        Assert.Single(application.GetServices<IFallbackRequestHandlerProvider>()
            .OfType<StaticContentMountProvider>());

        Assert.Empty(application.GetServices<IWebExecutionRequestHandlerProvider>()
            .OfType<StaticContentMountProvider>());
    }

    /// <summary>
    /// Nothing registers static content unless the application asked for it. The module is the
    /// opt-in, and the web module on its own must leave no mount behind - which is the difference
    /// between a service that cannot serve a file and one that serves whatever is in wwwroot.
    /// </summary>
    [Fact]
    public void TheWebModuleAloneRegistersNoMount() {
        var services = new ServiceCollection();

        new HardenedWebModule().ConfigureServices(services);

        using var application = services.BuildServiceProvider();

        Assert.Empty(application.GetServices<IFallbackRequestHandlerProvider>());
    }

    #endregion
}
