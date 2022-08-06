using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime;
using Hardened.Function.Lambda.Runtime.DependencyInjection;
using Hardened.IntegrationTests.Function.Lambda.SUT.Functions;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Logging;
using Hardened.Templates.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Function.Lambda.SUT
{
    [LambdaFunctionApplication]
    public class Application
    {
        public Application(IEnvironment environment, Action<IServiceCollection>? overrideDependencies)
        {
            var loggerFactory = LoggerFactory.Create(LambdaLoggerHelper.CreateAction(environment, "Hardened.IntegrationTests.Web.Lambda.SUT"));
            Provider = CreateServiceProvider(environment, overrideDependencies, loggerFactory);
            ApplicationLogic.StartWithWait(Provider, null, 15);
        }

        public IServiceProvider CreateServiceProvider(IEnvironment environment, Action<IServiceCollection>? overrideDependencies, ILoggerFactory loggerFactory)
        {
            var serviceCollection = new ServiceCollection();

            serviceCollection.TryAddTransient(typeof(ILogger<>), typeof(LoggerImpl<>));
            serviceCollection.AddSingleton<ILoggerFactory>(_ => loggerFactory);

            StandardDependencies.Register(environment, serviceCollection);
            RequestRuntimeDI.Register(environment, serviceCollection);
            TemplateDI.Register(environment, serviceCollection);
            LambdaFunctionDI.Register(environment, serviceCollection);

            DependencyRegistry<Application>.ApplyRegistration(environment, serviceCollection, this);

            LocalRegistration(environment, serviceCollection);

            overrideDependencies?.Invoke(serviceCollection);

            return serviceCollection.BuildServiceProvider();
        }

        private void LocalRegistration(IEnvironment environment, ServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient(typeof(PersonFunctions));
        }

        public IServiceProvider Provider { get; }
    }
}