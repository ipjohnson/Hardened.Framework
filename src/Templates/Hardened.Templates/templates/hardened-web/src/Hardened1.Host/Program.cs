using Hardened.Shared.Runtime.Application;
using Hardened1.Host;
#if (kestrel)
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
#endif

// Listens on 5080. Override with PORT.
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured) ? configured : 5080;

#if (kestrel)
var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

// Registered by the application, not the framework: only the application knows where its
// environment name and arguments come from. HARDENED_ENVIRONMENT names it, or "development".
services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(port));

// Printed, so the address is read rather than guessed. The ASP.NET host does this for you.
app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Hardened1.Host")
    .LogInformation("Listening on http://localhost:{Port}", port);

await app.RunAsync();
#endif
#if (aspnet)
var builder = WebApplication.CreateBuilder(args);

// Registered by the application, not the framework: only the application knows where its
// environment name and arguments come from. HARDENED_ENVIRONMENT names it, or "development".
builder.Services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

app.UseHardened();

// Startup services are run by the Kestrel and function hosts, and by the test harness. The
// ASP.NET bridge does not run them, so anything registered as IStartupService - a global
// filter, a warmed cache - needs this line to run at all.
await ApplicationLogic.Start(app.Services, null);

// The URL goes here rather than through builder.WebHost.UseUrls, which needs a
// Microsoft.AspNetCore.Hosting using this project does not otherwise want.
app.Run($"http://localhost:{port}");
#endif
