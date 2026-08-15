using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.OpenApi.SUT;

[HardenedModule]
[HardenedWebModule]
public partial class OpenApiTestApp : IServiceCollectionConfiguration {

    /// <summary>
    /// Serves the specification this application was generated from, verbatim.
    /// </summary>
    /// <remarks>
    /// The document is a build input here, so there is nothing to emit and nothing that can drift.
    /// The path says <c>.yaml</c> because the source does: converting it to fit a conventional
    /// <c>/openapi.json</c> would put an emitter back in a path that exists to have none.
    /// </remarks>
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IWebExecutionRequestHandlerProvider>(
            new OpenApiDocumentProvider(
                PetstoreSpecification.Document,
                "/openapi.yaml",
                PetstoreSpecification.ContentType));
    }
}
