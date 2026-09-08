using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.IntegrationTests.RieInvoke.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The direct invoke fixture hosted as a deployed function is. The invoke adapter routes on the
// function's own name, which the container sets through AWS_LAMBDA_FUNCTION_NAME.

var services = new ServiceCollection();

// Through the Lambda logger, as a deployed function does. A console provider writes from its own
// background thread through the Console.Out that Amazon.Lambda.RuntimeSupport replaced with a
// writer it locks on, and it takes that lock to print an unhandled exception before reporting the
// invocation failed. The two deadlock, and the invocation then hangs rather than failing.
services.AddLogging(builder => builder.AddLambdaLogger().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<IOrderLog, ObservedOrderLog>();

new InvokeTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
