using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Http;

/// <summary>
/// Registers the HTTP adapter for Lambda, applied to an application as <c>[LambdaHttpModule]</c>.
/// </summary>
/// <remarks>
/// Selected by the web verbs through <c>HardenedHttpModule</c>, which is what makes
/// <c>[Get("/orders")]</c> run on Lambda without naming a front door - the same handler runs on
/// Kestrel and inside ASP.NET Core with only this binding changing.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class LambdaHttpModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, LambdaHttpAdapter>();
    }
}
