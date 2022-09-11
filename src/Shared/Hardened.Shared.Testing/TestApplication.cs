using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing
{
    public class TestApplication : IApplicationRoot
    {
        public TestApplication(IApplicationModule testModule, string logNs, IEnvironment environment, Action<IServiceCollection>? overrideDependencies)
        {
            var loggerFactory = LoggerFactory.Create(builder => {});
            Provider = CreateServiceProvider(testModule, environment, overrideDependencies, loggerFactory);
            ApplicationLogic.StartWithWait(Provider, null, 15);
        }

        private IServiceProvider CreateServiceProvider(IApplicationModule applicationModule, IEnvironment environment,
            Action<IServiceCollection>? overrideDependencies, ILoggerFactory loggerFactory)
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.TryAddTransient(typeof(ILogger<>), typeof(LoggerImpl<>));
            serviceCollection.AddSingleton(_ => loggerFactory);

            applicationModule.ConfigureModule(environment, serviceCollection);

            overrideDependencies?.Invoke(serviceCollection);

            return serviceCollection.BuildServiceProvider();
        }


        public IServiceProvider Provider { get; }
    }
}
