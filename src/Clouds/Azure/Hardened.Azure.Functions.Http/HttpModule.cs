using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Http;

/// <summary>
/// Registers the HTTP adapter, applied to an application as <c>[HttpModule]</c>.
/// </summary>
/// <remarks>
/// Selected by the web verbs through <c>HardenedHttpModule</c>, which is what makes
/// <c>[Get("/orders")]</c> run behind an HTTP trigger without naming it - the same handler runs
/// on Kestrel, inside ASP.NET Core and behind API Gateway with only this binding changing.
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class HttpModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new HttpAdapter());
    }

    public override bool Equals(object? obj) => obj is HttpModule;

    public override int GetHashCode() => typeof(HttpModule).GetHashCode();
}
