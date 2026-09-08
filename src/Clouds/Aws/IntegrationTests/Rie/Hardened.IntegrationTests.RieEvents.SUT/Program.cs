using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.IntegrationTests.RieEvents.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The event family fixture - a queue, a topic, a schedule and a bus event on one function - hosted
// as a deployed function is. This is the fixture where the payload peek chooses between adapters,
// and the container tier is where that choice runs inside the real runtime.

var services = new ServiceCollection();

// Through the Lambda logger, as a deployed function does. A console provider writes from its own
// background thread through the Console.Out that Amazon.Lambda.RuntimeSupport replaced with a
// writer it locks on, and it takes that lock to print an unhandled exception before reporting the
// invocation failed. The two deadlock, and the invocation then hangs rather than failing.
services.AddLogging(builder => builder.AddLambdaLogger().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<ITriggerLog, ObservedTriggerLog>();

new EventsTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
