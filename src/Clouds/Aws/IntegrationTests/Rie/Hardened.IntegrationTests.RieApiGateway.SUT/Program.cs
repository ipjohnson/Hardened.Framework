using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.ApiGateway.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The API Gateway fixture hosted as a deployed function is. Nothing to observe: the proxy response
// the invocation returns is the whole answer.

var services = new ServiceCollection();

// Through the Lambda logger, as a deployed function does. A console provider writes from its own
// background thread through the Console.Out that Amazon.Lambda.RuntimeSupport replaced with a
// writer it locks on, and it takes that lock to print an unhandled exception before reporting the
// invocation failed. The two deadlock, and the invocation then hangs rather than failing.
services.AddLogging(builder => builder.AddLambdaLogger().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new ApiGatewayTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
