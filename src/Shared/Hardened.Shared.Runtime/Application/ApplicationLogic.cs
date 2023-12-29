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
        IEnvironment environment,
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
    /// Execute startup logic 
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="startupTask"></param>
    /// <returns></returns>
    public static async Task<int> Start(IServiceProvider serviceProvider,
        Func<IServiceProvider, Task<bool>>? startupTask) {
        var startupTasks = new List<Task<bool>>();

        foreach (var startupService in serviceProvider.GetServices<IStartupService>()) {
            startupTasks.Add(startupService.Startup(serviceProvider));
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