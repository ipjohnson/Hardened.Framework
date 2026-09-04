using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application;

public class ApplicationLogic {
    /// <summary>
    /// Execute startup logic followed by application delegate logic
    /// </summary>
    /// <param name="environment"></param>
    /// <param name="serviceProvider"></param>
    /// <param name="startupTask"></param>
    /// <returns></returns>
    public static async Task<int> RunApplication(
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider,
        Func<IServiceProvider, Task<bool>>? startupTask) {
        var delegateProvider = serviceProvider.GetRequiredService<IApplicationDelegateProvider>();
        var delegateResult = await delegateProvider.ProvideDelegate(environment, serviceProvider);

        if (delegateResult.ShouldStartApp) {
            var result = await Start(serviceProvider, startupTask);

            if (result != 0) {
                return result;
            }
        }

        return await delegateResult.Delegate();
    }

    /// <summary>
    /// The providers whose registered startup services have run. A startup service appends to
    /// singletons the whole application shares - the middleware chain, the filter registry, the
    /// CORS configuration - so running the set a second time installs everything a second time.
    /// Both the host and the application can reach this method, so a second caller is an ordinary
    /// arrangement rather than a mistake.
    /// </summary>
    private static readonly ConditionalWeakTable<IServiceProvider, object> StartedProviders = new();

    /// <summary>
    /// Execute startup logic. The registered <see cref="IStartupService"/>s run on the first call
    /// for a given service provider; a later call skips them and reports on its own
    /// <paramref name="startupTask"/> alone.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="startupTask"></param>
    /// <returns></returns>
    public static async Task<int> Start(IServiceProvider serviceProvider,
        Func<IServiceProvider, Task<bool>>? startupTask) {
        var startupTasks = new List<Task<bool>>();

        if (StartedProviders.TryAdd(serviceProvider, StartedProviders)) {
            foreach (var startupService in serviceProvider.GetServices<IStartupService>()) {
                startupTasks.Add(startupService.Startup(serviceProvider));
            }
        }

        if (startupTask != null) {
            startupTasks.Add(startupTask(serviceProvider));
        }

        if (startupTasks.Count > 0) {
            await Task.WhenAll(startupTasks);
        }

        return startupTasks.All(t => t.Result) ? 0 : 1;
    }

    public static void StartWithWait(IServiceProvider serviceProvider, Func<IServiceProvider, Task<bool>>? startup,
        int timeoutInSeconds) {
        Start(serviceProvider, startup).Wait(timeoutInSeconds * 1000);
    }
}