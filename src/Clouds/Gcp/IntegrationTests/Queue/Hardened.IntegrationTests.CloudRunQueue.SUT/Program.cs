using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.IntegrationTests.CloudRunQueue.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The queue fixture, hosted as a deployed Cloud Run service is: Kestrel on the port Cloud Run
// names, every address, and a Pub/Sub push subscription whose endpoint is this service. Nothing
// here knows whether the push comes from Pub/Sub or from the emulator in the container tier - the
// same Program runs on Cloud Run.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// The seam the tests observe through. A [Mock] cannot reach into another process, so the store
// prints what it was given and the test reads the container's output.
services.AddSingleton<IOrderStore, ObservedOrderStore>();

new CloudRunQueueApp().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

// Printed rather than logged so a harness can wait on it without depending on log configuration.
Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

// Rather than app.RunAsync(): that returns on ProcessExit, which is what SIGTERM raises when
// nothing has registered for it, and the process then exits before the server has drained. The
// container tier's ShutdownTests saw a request in flight cut off that way.
await CloudRunHost.RunAsync(app);
