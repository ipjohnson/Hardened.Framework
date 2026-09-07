using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Aws.Lambda.Runtime.Modules;

/// <summary>
/// Registers the direct-invoke adapter, applied to an application as <c>[InvokeModule]</c>.
/// </summary>
/// <remarks>
/// The only module here whose adapter references no AWS event package, because a direct invocation
/// has no envelope to bind. It is also the only family that must not share a function with another:
/// a caller's payload is not self-describing, so nothing can tell it apart from an event by
/// inspection.
/// </remarks>
[DependencyModule]
[LambdaRuntimeModule]
public partial class InvokeModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IPayloadAdapter, InvokeAdapter>();
    }
}
