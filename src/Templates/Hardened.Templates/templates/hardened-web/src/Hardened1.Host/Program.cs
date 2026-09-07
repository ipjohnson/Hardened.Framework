using Hardened.Shared.Runtime.Application;
using Hardened1.Host;
#if (kestrel)
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#endif
#if (lambda)
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#endif
#if (aspnet)
using Hardened.Web.AspNetCore.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
// WaitForShutdownAsync is an IHost extension rather than a WebApplication member.
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#endif

#if (!lambda)
// Listens on 5080. Override with PORT.
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var configured) ? configured : 5080;
#endif

// Registered by the application, not the framework: only the application knows where its
// environment name and arguments come from. HARDENED_ENVIRONMENT names it, or "development".
var environment = new EnvironmentImpl(arguments: args);

#if (kestrel)
var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services,
    kestrel => kestrel.ListenAnyIP(port));

// Started rather than run, so the address is printed once the server is actually listening
// rather than just before. RunAsync below sees IsStarted and waits for shutdown rather than
// starting a second time.
await app.StartAsync();

// Printed, so the address is read rather than guessed. The ASP.NET host does this for you.
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Hardened1.Host");

logger.LogInformation("Listening on http://localhost:{Port}", port);
#if (OpenApiUi)

// Where to start, rather than an address and a guess about what is under it. Gated on the
// environment because the reference page is: printing a URL that answers 404 is worse than
// printing nothing.
if (environment.Matches("development")) {
    logger.LogInformation("Browse http://localhost:{Port}/docs to access your API.", port);
}
#endif

await app.RunAsync();
#endif
#if (aspnet)
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

// Runs the registered startup services as well, so an IStartupService - a global filter, a
// warmed cache - runs here as it does under the Kestrel host.
app.UseHardened();

// The URL is set here rather than through builder.WebHost.UseUrls, which needs a
// Microsoft.AspNetCore.Hosting using this project does not otherwise want.
app.Urls.Add($"http://localhost:{port}");

await app.StartAsync();
#if (OpenApiUi)

// ASP.NET prints the address itself, so this adds only the part it cannot know about. Gated on the
// environment because the reference page is.
if (environment.Matches("development")) {
    app.Logger.LogInformation("Browse http://localhost:{Port}/docs to access your API.", port);
}
#endif

await app.WaitForShutdownAsync();
#endif
#if (lambda)
// Started by the Lambda service, this does nothing and the bootstrap reads the address the service
// set. Started from an IDE or `dotnet run`, there is no such address, so this brings up the AWS
// Lambda Test Tool with its API Gateway emulator in front and sets the same variable the service
// would have. The application then answers on PORT, 5080 by default, exactly as the Kestrel host
// does - which is what makes the two hosts interchangeable to a browser.
//
// Delete it and the application still deploys; only running it locally stops working.
using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application), apiGateway: true);

var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

services.AddHardenedEnvironment(environment);

new Application().PopulateServiceCollection(services);

// No server and no port. API Gateway is the server, and what this starts is the loop that reads
// the requests it forwards.
await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
#endif
