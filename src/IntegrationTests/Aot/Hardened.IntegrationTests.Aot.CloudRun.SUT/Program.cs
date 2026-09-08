using System.Text.Json.Serialization.Metadata;
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.IntegrationTests.Aot.CloudRun.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A Hardened Cloud Run service published with Native AOT. The Kestrel sibling proves a native
// binary can serve a web route and the Lambda sibling that one can serve an invocation; this one
// serves both over one socket, which is what a Cloud Run service is: a Pub/Sub push arrives as an
// HTTP request, the front door recognises it ahead of routing and unwraps it into the trigger
// request the composite dispatch hands to the function path, while a plain GET goes to the web
// path. The CI probe posts one of each and reads what the handler printed.
//
// The same Program.cs a deployed service has. PORT names the port and SIGTERM ends the process,
// so the probe sets the one and sends the other.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// What AotSerializerModule resolves models through. Without it the serializers throw rather than
// falling back to reflection, which is the difference this project exists to hold.
services.AddSingleton<IJsonTypeInfoResolver>(AotContext.Default);

services.AddSingleton<IOrderSink, OrderSink>();

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

// Printed rather than logged so the probe can wait on it without depending on log configuration.
Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await CloudRunHost.RunAsync(app);
