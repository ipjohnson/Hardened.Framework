using Hardened.IntegrationTests.Kestrel.SUT;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Hardened on Kestrel with no ASP.NET Core pipeline. Compare with the ASP.NET-hosted equivalent
// in ../Web/Hardened.IntegrationTests.WebApp.SUT/Program.cs, which builds a WebApplication and
// calls UseHardened().

var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 5000;

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenLocalhost(port));

await app.StartAsync();

// Printed rather than logged so a harness can wait on it without depending on log configuration.
Console.WriteLine($"LISTENING {string.Join(",", app.Addresses)}");

await app.RunAsync();
