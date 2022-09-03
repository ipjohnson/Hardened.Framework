using Amazon.Lambda.Core;
using Hardened.Function.Lambda.Runtime;
using Hardened.Function.Lambda.Runtime.DependencyInjection;
using Hardened.IntegrationTests.Function.Lambda.SUT.Functions;
using Hardened.IntegrationTests.Function.Lambda.SUT.Services;
using Hardened.Requests.Runtime.DependencyInjection;
using Hardened.Shared.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Logging;
using Hardened.Templates.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Hardened.IntegrationTests.Function.Lambda.SUT
{
    [HardenedStartup]
    public partial class Application
    {
        private void RegisterDependencies(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<PersonFunctions>();
        }
    }
}