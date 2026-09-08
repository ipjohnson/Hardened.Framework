using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Requests.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Azure.Functions.Runtime.Modules;

/// <summary>
/// The invocation handler, which every function needs whatever it is triggered by.
/// </summary>
/// <remarks>
/// <para>
/// Composed onto every adapter module rather than applied by an application, the way
/// <c>LambdaRuntimeModule</c> is: whichever trigger pulled an adapter in also pulls this, so an
/// adapter is never registered with nothing to drive it.
/// </para>
/// <para>
/// <c>TryAddSingleton</c> because several adapter modules compose it and modules deduplicate by
/// equality, not by what they register. <c>[HardenedRequestModule]</c> brings the request pipeline,
/// without which this registers a host and nothing to run.
/// </para>
/// </remarks>
[DependencyModule]
[HardenedRequestModule]
public partial class FunctionsRuntimeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<FunctionsInvocationHandler>();
    }
}
