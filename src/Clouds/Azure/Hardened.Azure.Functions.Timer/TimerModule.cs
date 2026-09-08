using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Timer;

/// <summary>
/// Registers the timer adapter, applied to an application as <c>[TimerModule]</c>.
/// </summary>
/// <remarks>
/// Selected by <c>[Timer]</c> on a handler through the <c>HardenedTimerModule</c> build property.
/// No settings: the schedule is an app setting named after the trigger, read by the host, so
/// there is nothing a deployment has to say here.
/// </remarks>
[DependencyModule]
[FunctionsRuntimeModule]
public partial class TimerModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<ITriggerAdapter>(new TimerAdapter());
    }

    public override bool Equals(object? obj) => obj is TimerModule;

    public override int GetHashCode() => typeof(TimerModule).GetHashCode();
}
