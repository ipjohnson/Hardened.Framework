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

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<ITriggerLog, ObservedTriggerLog>();

new EventsTestApp().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
