using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.ApiGateway.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The API Gateway fixture hosted as a deployed function is. Nothing to observe: the proxy response
// the invocation returns is the whole answer.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new ApiGatewayTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
