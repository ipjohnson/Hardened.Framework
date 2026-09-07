#if (aws)
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Hardened1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// The whole of the entry point. Nothing here names the trigger: the handler's attribute decided
// which adapter got registered, and this starts the loop that feeds it.
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
