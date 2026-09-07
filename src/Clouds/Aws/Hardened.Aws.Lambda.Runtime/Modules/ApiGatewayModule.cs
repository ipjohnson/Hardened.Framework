using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// Registers the API Gateway adapter, applied to an application as <c>[ApiGatewayModule]</c>.
/// </summary>
/// <remarks>
/// Selected by the web verbs through <c>HardenedHttpModule</c>, which is what makes
/// <c>[Get("/orders")]</c> run behind API Gateway without naming it - the same handler runs on
/// Kestrel and inside ASP.NET Core with only this binding changing.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class ApiGatewayModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, ApiGatewayAdapter>();
    }
}
