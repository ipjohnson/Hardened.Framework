using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.IntegrationTests.RieInvoke.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The direct invoke fixture hosted as a deployed function is. The invoke adapter routes on the
// function's own name, which the container sets through AWS_LAMBDA_FUNCTION_NAME.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<IOrderLog, ObservedOrderLog>();

new InvokeTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
