using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.IntegrationTests.CloudRunTimer.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The scheduled fixture, hosted as a deployed Cloud Run service is: Kestrel on the port Cloud Run
// names, and a Scheduler job whose target is this service's timer route.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<ITriggerLog, ObservedTriggerLog>();

new CloudRunTimerApp().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await CloudRunHost.RunAsync(app);
