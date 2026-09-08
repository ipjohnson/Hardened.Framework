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

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// The seam the tests observe through. A [Mock] cannot reach into another process, so the store
// prints what it was given and the test reads the container's output.
services.AddSingleton<IOrderStore, ObservedOrderStore>();

new SqsTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
