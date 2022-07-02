using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Runtime.Application
{
    public class ApplicationLogic
    {
        public static async Task Start(IServiceProvider serviceProvider)
        {
            foreach (var startupService in serviceProvider.GetServices<IStartupService>())
            {
                await startupService.Startup(serviceProvider);
            }
        }

        public static void StartWithWait(IServiceProvider serviceProvider, int timeoutInSeconds)
        {
            Start(serviceProvider).Wait(timeoutInSeconds * 1000);
        }
    }
}
