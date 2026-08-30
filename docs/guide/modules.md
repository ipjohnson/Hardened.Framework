# Modules

A module is the unit Hardened composes applications out of. It is a `partial class` marked
`[HardenedModule]`, and the generator writes the other half.

```csharp
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
public partial class Application { }
```

## What the generator emits

**The module itself** gains `IDependencyModule` and a public `PopulateServiceCollection`:

```csharp
public partial class Application : IDependencyModule {
    public void PopulateServiceCollection(IServiceCollection services) { /* … */ }
}
```

That method is the seam into any host. ASP.NET Core calls it with `builder.Services`; a console
application calls it with a collection it made itself; the test framework calls it for you.

**A companion attribute class** named after the module, so one module can import another:

```csharp
public partial class ApplicationAttribute : Attribute, IDependencyModuleProvider { /* … */ }
```

Every runtime in the framework is spelled as an attribute for this reason.
`[AspNetCoreRuntime]`, `[HardenedWebModule]` and `[DynamoDbModule]` are each the generated companion
of a module class of that name, and yours works the same way.

## Composing modules

Attribute one module with another and its registrations come along:

```csharp
[HardenedModule]
[HardenedWebModule]      // routing, static content, CORS
[DynamoDbModule]         // IDynamoDbClientProvider
public partial class Application { }
```

Order does not matter, and importing the same module twice is not a problem — modules deduplicate by
equality.

### Splitting an application into libraries

A library is a module in another assembly. It carries its own handlers, services and route prefix,
and the application picks it up with one attribute:

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
[AspNetCoreRuntime]
[BillingLibrary(Tenant = "acme")]
public partial class Application { }
```

Public settable properties on the module become properties on the generated attribute, which is how
`Tenant` is set at the import site.

::: tip Routes come with the library
`[BasePath]` on the library module prefixes every route in that assembly. The application does not
list the library's routes.
:::

## Programmatic registration

A module that needs to compute something implements `IServiceCollectionConfiguration` and gets the
collection directly:

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

If the decision depends on the environment, implement
`IEnvironmentServiceCollectionConfiguration` instead and the environment is handed to you:

```csharp
public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment) {
    if (environment.EnvironmentName == "development") {
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();
    }
}
```

::: tip Conditional registration is usually an attribute
`[IfEnvironment]`, `[IfNotEnvironment]`, `[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` come
from DependencyModules and are resolved during the build. Reach for the interface only when the
condition is not expressible as one.
:::

## Startup work

Register an `IStartupService` and it runs once, after the provider is built and before the
application serves anything:

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

Every registered startup service is launched together and awaited as a group, so they must not
depend on each other's ordering. Returning `false`, or throwing, fails startup.

## Self-hosting entry points

An ASP.NET Core application builds its own host, so the module only needs
`PopulateServiceCollection`. A console application or a Lambda function has no host to build, so the
generator writes one: constructors, a root service provider, and `IApplicationRoot`.

```csharp
var application = new Application(args);

var result = await application.Run();

await application.DisposeAsync();

return result;
```

For those entry points, three method names on the module are recognised and called if present:

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

::: warning These are matched by name, not by an interface
A typo in `ConfigureLogging` is not a compile error — it is a method nobody calls. If a hook appears
to do nothing, check the spelling against the table above, then check the generated entry point
under `obj/` for whether it is mentioned.
:::

## The relationship to DependencyModules

Hardened's module system is
[DependencyModules](https://ipjohnson.github.io/DependencyModules/) underneath.
`[HardenedModule]` is a Hardened-flavoured `[DependencyModule]`, and the registration attributes —
`[SingletonService]`, `[ScopedService]`, `[TransientService]` — come straight from that package,
along with conventions, decorators, interception and environment-conditional registration.
[Registering services](/guide/services) covers the parts you will reach for most.
