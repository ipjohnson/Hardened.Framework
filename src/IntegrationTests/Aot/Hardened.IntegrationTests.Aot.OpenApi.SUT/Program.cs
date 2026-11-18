using System.Text.Json.Serialization.Metadata;
using Hardened.IntegrationTests.Aot.OpenApi.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 5000;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// tenants.yaml declares no named schemas, so the build task emits no resolver for it and the
// response's List<string> resolves through this one.
services.AddSingleton<IJsonTypeInfoResolver>(AotContext.Default);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenLocalhost(port)
);

await app.StartAsync();

Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await app.RunAsync();
