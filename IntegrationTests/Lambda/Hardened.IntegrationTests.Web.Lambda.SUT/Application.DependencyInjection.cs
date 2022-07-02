using Hardened.IntegrationTests.Web.Lambda.SUT.Controllers;
using Hardened.IntegrationTests.Web.Lambda.SUT.Services;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime;
using Hardened.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.IntegrationTests.Web.Lambda.SUT
{
    public partial class Application
    {

        private IServiceProvider CreateServiceProvider(Action<IServiceCollection>? overrideDependencies)
        {
            var serviceCollection = new ServiceCollection();

            RegisterDependencies(serviceCollection);
            RegisterLocalDependencies(serviceCollection);

            overrideDependencies?.Invoke(serviceCollection);
            
            return serviceCollection.BuildServiceProvider();
        }

        private static void RegisterLocalDependencies(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddTransient<HomeController>();
            serviceCollection.TryAddTransient<PersonController>();
            serviceCollection.TryAddSingleton<IWebExecutionRequestHandlerProvider, RoutingTable>();
            serviceCollection.TryAddSingleton<ITemplateExecutionHandlerProvider, Application.TemplateProvider>();
            serviceCollection.TryAddTransient<IMathService, MathService>();
            serviceCollection.TryAddSingleton<IPersonService, PersonService>();
        }

        private static void RegisterDependencies(IServiceCollection serviceCollection)
        {
            StandardDependencies.Add(serviceCollection);
            LambdaWebDI.Register(serviceCollection);
            RequestRuntimeDI.Register(serviceCollection);
            TemplateDI.Register(serviceCollection);
            WebRuntimeDI.Register(serviceCollection);
        }
    }
}
