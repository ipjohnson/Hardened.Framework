using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.IntegrationTests.CloudRunBlob.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The blob fixture, hosted as a deployed Cloud Run service is: Kestrel on the port Cloud Run
// names, reached by an Eventarc trigger on the bucket or by a notification through Pub/Sub.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
services.AddSingleton<IUploadSink, ObservedUploadSink>();

new CloudRunBlobApp().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await CloudRunHost.RunAsync(app);
