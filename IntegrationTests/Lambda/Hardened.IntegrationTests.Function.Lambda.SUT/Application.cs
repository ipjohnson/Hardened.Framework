using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime;
using Hardened.Function.Lambda.Runtime.DependencyInjection;
using Hardened.IntegrationTests.Function.Lambda.SUT.Functions;
using Hardened.IntegrationTests.Function.Lambda.SUT.Services;
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
    public partial class Application
    {
        public Application(IEnvironment environment, Action<IServiceCollection>? overrideDependencies)
        {
            var loggerFactory = LoggerFactory.Create(LambdaLoggerHelper.CreateAction(environment, "Hardened.IntegrationTests.Web.Lambda.SUT"));
            Provider = CreateServiceProvider(environment, overrideDependencies, loggerFactory);
            ApplicationLogic.StartWithWait(Provider, null, 15);
        }

        private void RegisterDependencies(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<PersonFunctions>();
        }

        public IServiceProvider Provider { get; }
    }
}