using Hardened.IntegrationTests.Web.SUT;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Runtime.Compression;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Handlers;
using Hardened.Requests.Caching.Memory;
using Hardened.Web.Runtime.Compression;
using Hardened.Web.Runtime.OpenApi;

namespace Hardened.IntegrationTests.WebApp.SUT;

/// <remarks>
/// <para>
/// <c>[Enable&lt;OpenApiDocumentPublishing&gt;]</c> is what embeds the document the web generator wrote
/// from this application's own routes and serves it at <c>/openapi.json</c>. It replaces the
/// registration this module used to make by hand, which had to live here rather than in
/// <c>CreateBuilder</c> - that helper is only used by Program.cs, and the test host calls
/// <c>PopulateServiceCollection</c> directly.
/// </para>
/// <para>
/// The reference page is installed twice, at different paths, because that is the arrangement worth
/// covering: <c>HardenedOpenApiUi</c> keys its equality on <c>Path</c> so that a service publishing
/// several specifications gets a page for each, and nothing but a second install proves the module
/// loads more than once.
/// </para>
/// </remarks>
[HardenedModule]
[WebLibrary(Test = "test")]
[Enable<OpenApiDocumentPublishing>]
[Enable<HardenedCompression>]
[HardenedOpenApiUi(Title = "Integration Tests")]
[HardenedOpenApiUi(Path = "/docs/internal", Title = "Internal", DocumentPath = "/internal.json")]
[HardenedMemoryResponseCache]
[AspNetCoreRuntime]
public partial class Application : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        // The supported testing source, through the same seam and middleware a production
        // authentication source uses - this fixture carried its own copy of both until they
        // shipped.
        services.AddSingleton<IPrincipalSource, TestGrantsPrincipalSource>();

        // Small enough for a test to exceed with a body it can build in a line. Only a compressed
        // body is measured against it.
        services.ConfigureCompression(compression => compression.MaxDecompressedRequestBytes = 4096);
    }

    public static WebApplicationBuilder CreateBuilder(string[] args) {
        var hardenedApp = new Application();
        var environment = new EnvironmentImpl(arguments:  args);
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddTransient<IHardenedEnvironment>(_ => environment);

        hardenedApp.PopulateServiceCollection(builder.Services);

        return builder;
    }
}