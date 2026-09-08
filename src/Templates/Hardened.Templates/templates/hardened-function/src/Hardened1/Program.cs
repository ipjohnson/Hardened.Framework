#if (aws)
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The whole of the entry point. Nothing here names the trigger: the handler's attribute decided
// which adapter got registered, and this starts the loop that feeds it.

// Started by the Lambda service, this does nothing and the bootstrap reads the address the service
// set. Started from an IDE or `dotnet run`, there is no such address, so this brings up the AWS
// Lambda Test Tool and sets the same variable the service would have - which is what puts F5 on
// the process Lambda would have started, with no second project and no local branch below.
//
// Delete it and the function still deploys; only running it locally stops working.
using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application));

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));

// What the framework reads configuration and the environment through. A deployed function gets its
// settings from the environment, so this is where the process arguments enter.
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

// The container is built before the loop starts, on purpose: a missing registration is then a cold
// start that fails immediately with the name of what was missing, rather than the first invocation
// of the day failing while every later one on a warm sandbox succeeds.
await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
#endif
#if (gcp)
using Hardened.Gcp.CloudRun.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Hardened1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The whole of the entry point, and the Cloud Run container contract: listen on PORT, drain on
// SIGTERM. Nothing here names the trigger: the handler's attribute decided which adapter got
// registered, and the front door that adapter teaches runs ahead of routing on this socket.

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true));

// What the framework reads configuration and the environment through. A deployed service gets its
// settings from the environment, so this is where the process arguments enter.
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

// Every interface on the port Cloud Run names in PORT, 8080 when it is unset - which is what a
// container started by hand sees, so `dotnet run` answers on 8080 too.
await using var app = HardenedKestrelApplication.Create(services, CloudRunHost.Listen);

await app.StartAsync();

Console.WriteLine($"Listening on {string.Join(", ", app.Addresses)}");

// SIGTERM is how Cloud Run retires an instance, ten seconds before SIGKILL. This waits for it and
// gives what is in flight those ten seconds; the plain RunAsync returns on ProcessExit and the
// process exits before the server has drained.
await CloudRunHost.RunAsync(app);
#endif
