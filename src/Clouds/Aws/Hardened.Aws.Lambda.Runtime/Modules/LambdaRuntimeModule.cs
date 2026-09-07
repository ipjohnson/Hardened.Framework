using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Requests.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// The invocation loop, which every Lambda function needs whatever it is triggered by.
/// </summary>
/// <remarks>
/// <para>
/// Composed onto every adapter module rather than applied by an application, the way
/// <c>AspNetCoreRuntime</c> composes <c>[HardenedWebModule]</c>. An adapter with nothing to drive it
/// registers a seam nothing calls, and the failure would be at the first invocation rather than at
/// startup - so whichever trigger pulled an adapter in also pulls this.
/// </para>
/// <para>
/// <c>TryAddSingleton</c> because several adapter modules compose it and modules deduplicate by
/// equality, not by what they register.
/// </para>
/// <para>
/// <b>It brings the request pipeline with it.</b> <c>[HardenedRequestModule]</c> is what registers
/// <c>IRequestExecutor</c> and everything the chain resolves. Without it this module registered a
/// host and nothing to run, and the failure landed on the first invocation rather than at startup -
/// the identical omission <c>AspNetCoreRuntime</c> shipped with, and <c>LambdaWebModule</c> before
/// it.
/// </para>
/// </remarks>
[DependencyModule]
[HardenedRequestModule]
public partial class LambdaRuntimeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<LambdaInvocationHandler>();
    }
}
