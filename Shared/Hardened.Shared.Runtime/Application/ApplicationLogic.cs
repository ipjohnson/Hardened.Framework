using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application
{
    public class ApplicationLogic
    {
        public static Task Start(IServiceProvider serviceProvider, Func<IServiceProvider, Task>? startupTask)
        {
            var startupTasks = new List<Task>();

            foreach (var startupService in serviceProvider.GetServices<IStartupService>())
            {
                startupTasks.Add(startupService.Startup(serviceProvider));
            }

            if (startupTask != null)
            {
                startupTasks.Add(startupTask(serviceProvider));
            }

            if (startupTasks.Count > 0)
            {
                return Task.WhenAll(startupTasks);
            }

            return Task.CompletedTask;
        }

        public static void StartWithWait(IServiceProvider serviceProvider, Func<IServiceProvider, Task>? startup, int timeoutInSeconds)
        {
            Start(serviceProvider, startup).Wait(timeoutInSeconds * 1000);
        }
    }
}
