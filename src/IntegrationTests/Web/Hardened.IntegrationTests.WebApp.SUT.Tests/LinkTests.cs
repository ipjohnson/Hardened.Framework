using Hardened.Requests.Abstract.Links;
using Hardened.IntegrationTests.WebApp.SUT;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// The links the generator writes from this application's own routes.
/// </summary>
/// <remarks>
/// <para>
/// The names come from the controller and the method rather than from a route name someone
/// declared, so a rename is a compile error at every call site. This file existing at all is part
/// of the assertion: every call below binds against a generated member, and a route that stopped
/// existing would stop this project compiling rather than fail when someone followed the link.
/// </para>
/// </remarks>
public class LinkTests {

    /// <summary>
    /// The path, with no idea where the application is deployed. This is the form for a caller who
    /// wants the route rather than something a client can call.
    /// </summary>
    [HardenedTest]
    public Task RoutesBuildThePathFromTheTemplate(ITestWebApp testWebApp) {
        Assert.Equal("/binding/path/42", Application.Routes.Binding.FromPath("42"));
        Assert.Equal("/binding/pair/a/b", Application.Routes.Binding.FromMultiplePathTokens("a", "b"));
        Assert.Equal("/", Application.Routes.Home.HelloWorld());

        return Task.CompletedTask;
    }

    /// <summary>
    /// A token's value is escaped, because a value containing a separator would otherwise change
    /// which route the link points at - the failure a typed builder exists to prevent, arriving by
    /// another door.
    /// </summary>
    [HardenedTest]
    public Task ATokenValueIsEscaped(ITestWebApp testWebApp) {
        Assert.Equal("/binding/path/a%2Fb", Application.Routes.Binding.FromPath("a/b"));

        return Task.CompletedTask;
    }

    /// <summary>
    /// A typed token is formatted with the invariant culture, so the same code produces the same
    /// URL on a machine with a different locale.
    /// </summary>
    [HardenedTest]
    public Task ATypedTokenIsFormattedInvariantly(ITestWebApp testWebApp) {
        Assert.Equal("/binding/path-typed/-7", Application.Routes.Binding.TypedPathToken(-7));

        return Task.CompletedTask;
    }

    /// <summary>
    /// The links type is in the container, so a handler can take it as a constructor parameter.
    /// </summary>
    [HardenedTest]
    public Task TheLinksTypeResolvesFromTheContainer(ITestWebApp testWebApp) {
        Assert.NotNull(testWebApp.RootServiceProvider.GetRequiredService<Application.Links>());

        return Task.CompletedTask;
    }

    /// <summary>
    /// And it goes through the link context, which is what makes a link correct on a host that
    /// strips a prefix before the application sees the path - API Gateway's stage.
    /// </summary>
    [HardenedTest]
    public Task ALinkGoesThroughTheLinkContext(ITestWebApp testWebApp) {
        var links = new Application.Links(new StageContext());

        Assert.Equal("/prod/binding/path/42", links.Binding.FromPath("42"));
        Assert.Equal("https://api.example.com/prod/binding/path/42", links.Binding.FromPathAbsolute("42"));

        return Task.CompletedTask;
    }

    /// <summary>The default context is the identity, which is right for a host serving the root.</summary>
    [HardenedTest]
    public Task TheDefaultContextLeavesThePathAlone(ITestWebApp testWebApp) {
        var links = testWebApp.RootServiceProvider.GetRequiredService<Application.Links>();

        Assert.Equal("/binding/path/42", links.Binding.FromPath("42"));

        return Task.CompletedTask;
    }

    /// <summary>A host that serves the application under a prefix and knows its own address.</summary>
    private class StageContext : ILinkContext {
        public string BasePath => "/prod";

        public string Scheme => "https";

        public string Host => "api.example.com";

        public string Resolve(string path) => BasePath + path;

        public string Absolute(string path) => Scheme + "://" + Host + Resolve(path);
    }

    /// <summary>
    /// The routes a library module declares are reachable from the application's own links type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Links are generated per module, so <c>WebLibrary</c>'s routes live on
    /// <c>WebLibrary.Links</c> rather than on <c>Application.Links</c>. A generated template base
    /// hard-types its <c>Links</c> property to the application's, so before this an application
    /// whose routes all lived in libraries handed its views an empty links type and
    /// <c>@Links.Something.Route()</c> did not compile - the build-time guarantee was unavailable
    /// to exactly the applications that split into libraries.
    /// </para>
    /// <para>
    /// This test compiling is most of the assertion, as with the rest of this file: the property
    /// and the method under it are both generated, and either one going away breaks the build.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public Task AnImportedModulesRoutesAreReachableFromTheApplicationsLinks(Application.Links links) {
        Assert.Equal(
            "/web-library/string-methods/concat/a/b",
            links.WebLibrary.Some.Concat("a", "b"));

        return Task.CompletedTask;
    }

    /// <summary>
    /// The property is named for the module, which is the name already written at the import site.
    /// </summary>
    [HardenedTest]
    public Task TheImportedPropertyIsNamedForTheModule(Application.Links links) {
        Assert.IsType<Hardened.IntegrationTests.Web.SUT.WebLibrary.Links>(links.WebLibrary);

        return Task.CompletedTask;
    }
}
