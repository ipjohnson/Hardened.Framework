using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Rie.SUT;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The SQS fixture, hosted as a deployed function is: the bootstrap polls whatever
// AWS_LAMBDA_RUNTIME_API names. In the container tier that is the Runtime Interface Emulator inside
// the Lambda base image, and nothing here knows it - the same Program would run on Lambda.

var services = new ServiceCollection();

// Through the Lambda logger, as a deployed function does. A console provider writes from its own
// background thread through the Console.Out that Amazon.Lambda.RuntimeSupport replaced with a
// writer it locks on, and it takes that lock to print an unhandled exception before reporting the
// invocation failed. The two deadlock, and the invocation then hangs rather than failing.
services.AddLogging(builder => builder.AddLambdaLogger().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// The seam the tests observe through. A [Mock] cannot reach into another process, so the store
// prints what it was given and the test reads the container's output.
services.AddSingleton<IOrderStore, ObservedOrderStore>();

new SqsTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
