using System.Text.Json.Serialization.Metadata;
using Hardened.IntegrationTests.Aot.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A Hardened application that publishes with Native AOT. The point of the project is that this
// file, the framework behind it and the code its generators emit all survive ILC - and then serve
// a request, which is the part a warning-free build does not tell you.

var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 5000;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

// What AotSerializerModule resolves models through. An application publishing AOT supplies one of
// these; the serializers throw without it rather than falling back to reflection.
services.AddSingleton<IJsonTypeInfoResolver>(AotContext.Default);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenLocalhost(port));

await app.StartAsync();

// Printed rather than logged so a harness can wait on it without depending on log configuration.
Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await app.RunAsync();
