# Modules

A module is the unit an application is composed from. An application imports a library module
with one attribute, and the library's handlers, services, configuration and routes come along.

```csharp
// In the library assembly
[HardenedModule]
[HardenedWebModule]
[BasePath("/billing")]
public partial class BillingLibrary {
    public string Tenant { get; set; } = "default";
}
```

```csharp
// In the application
[HardenedModule]
[KestrelRuntime]
[BillingLibrary(Tenant = "acme")]
public partial class Application;
```

`[BillingLibrary]` is generated from the library's module class. `[KestrelRuntime]`,
`[HardenedWebModule]` and `[DynamoDbModule]` are the same thing: each is the companion attribute
of a module of that name.

## Declaring a module

```csharp
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
public partial class Application { }
```

`partial`, because the generator writes the other half:

- `IDependencyModule` and a public `PopulateServiceCollection(IServiceCollection)`. That method is
  the seam into any host. ASP.NET Core calls it with `builder.Services`, a console application
  calls it with a collection it made itself, and the test framework calls it for you.
- `ApplicationAttribute`, so another module can import this one. A public settable property on the
  module becomes a property on the attribute, which is how `Tenant` was set above.

## Composing modules

Attribute one module with another and its registrations come along:

```csharp
[HardenedModule]
[HardenedWebModule]      // routing, static content, CORS
[DynamoDbModule]         // IDynamoDbClientProvider
public partial class Application { }
```

Order does not matter. Importing the same module twice is harmless, because modules deduplicate
by equality.

A library module in another assembly carries its own handlers, services and route prefix.
`[BasePath]` on it prefixes every route in that assembly, so the application lists none of the
library's routes. See [Prefixing with BasePath](/guide/routing#prefixing-with-basepath).

## Registering by hand

Most registration is an attribute on the class; see
[Registering services](/guide/services). A module that has to compute a registration implements
`IServiceCollectionConfiguration` and gets the collection directly:

```csharp
using DependencyModules.Runtime.Interfaces;

[HardenedModule]
public partial class Application : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IBatchProcessorExceptionHandler, StrictExceptionHandler>();
    }

    // Optional. Runs after every module has registered, which is where decoration belongs.
    public void ConfigureDecorators(IServiceCollection services) { }
}
```

`IEnvironmentServiceCollectionConfiguration` is the same with the environment handed in:

```csharp
public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) {
    if (environment.EnvironmentName == "development") {
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();
    }
}
```

For a condition on the environment name, prefer `[IfEnvironment]` and its siblings. They are
decided during the build. See [Conditional registration](/guide/services#conditional-registration).

## Startup work

An `IStartupService` runs once, after the provider is built and before the application serves
anything:

```csharp
public interface IStartupService {
    Task<bool> Startup(IServiceProvider rootProvider);
}
```

```csharp
[SingletonService(As = typeof(IStartupService))]
public class WarmCaches : IStartupService {
    public async Task<bool> Startup(IServiceProvider rootProvider) {
        await rootProvider.GetRequiredService<IRateTable>().Load();

        return true;
    }
}
```

Every registered startup service is launched together and awaited as a group, so they cannot
depend on each other's order. Returning `false` or throwing fails startup.

## Self-hosting entry points

An ASP.NET Core application builds its own host, so the module only needs
`PopulateServiceCollection`. A console application or a Lambda function has no host to build, so
the generator writes one: constructors, a root service provider, and `IApplicationRoot`.

```csharp
var application = new Application(args);

var result = await application.Run();

await application.DisposeAsync();

return result;
```

For those entry points, three method names on the module are called if present:

| Method | Effect |
|---|---|
| `Task<bool> Startup(IServiceProvider)` | Runs alongside the registered `IStartupService`s |
| `void ConfigureLogging(ILoggingBuilder)` <br> `void ConfigureLogging(IHardenedEnvironment, ILoggingBuilder)` | Configures the logging builder |
| `LogLevel ConfigureLogLevel(IHardenedEnvironment)` | Sets the minimum level without touching the builder |

```csharp
[HardenedModule]
[CommandsLibrary]
public partial class Application {
    private void ConfigureLogging(IHardenedEnvironment environment, ILoggingBuilder builder) {
        builder.SetMinimumLevel(
            environment.Matches("production") ? LogLevel.Warning : LogLevel.Debug);
    }
}
```

::: warning Matched by name, not by an interface
A typo in `ConfigureLogging` is a method nobody calls, not a compile error. If a hook does
nothing, check the spelling against the table, then check the generated entry point under `obj/`
for whether it is mentioned.
:::

## DependencyModules

Hardened's module system is [DependencyModules](https://ipjohnson.github.io/DependencyModules/).
`[HardenedModule]` is a Hardened-flavoured `[DependencyModule]`, and `[SingletonService]`,
`[ScopedService]`, `[TransientService]`, conventions, decorators, interception and
environment-conditional registration all come from that package.

## Next

- [Registering services](/guide/services): the lifetime attributes
- [Configuration](/guide/configuration): the models a module carries
- [Environments](/guide/environments): what `[IfEnvironment]` reads
