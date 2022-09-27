using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Runtime.DependencyInjection
{
    public class FilterRegistryStartupService : IStartupService
    {
        private readonly Action<IGlobalFilterRegistry> _registryAction;

        public FilterRegistryStartupService(Action<IGlobalFilterRegistry> registryAction)
        {
            _registryAction = registryAction;
        }

        public Task Startup(IServiceProvider rootProvider)
        {
            var registry = rootProvider.GetRequiredService<IGlobalFilterRegistry>();

            _registryAction(registry);

            return Task.CompletedTask;
        }
    }
}
