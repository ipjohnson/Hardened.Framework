# Modules

A module is a `partial` class marked `[HardenedModule]`. The build writes an attribute for each
module, named after the class. A module imports another module by applying that module's
attribute.

The application is a module that imports the others. In the template, `Application` in
`src/Todos.Host` imports `TodosLibrary` from `src/Todos`, which holds the handlers:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[TodosLibrary]
public partial class Application;
```

The imported module's handlers, services and configuration models are registered wherever the
importing module is:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

Without `[TodosLibrary]` on `Application`, the application builds with no warning. `GET /todos/1`
then answers 404.

## Declare a module

`[HardenedModule]` is in the namespace `Hardened.Shared.Runtime.Attributes`, in the package
`Hardened.Shared.Runtime`. [From scratch](/guide/from-scratch) covers the packages, why the class is
`partial`, and the files the build generates.

A second `[HardenedModule]` class in the same project fails the build with the error `HRDR004`.

A module registers what its own project declares: the classes with a lifetime attribute, the
handlers and the routing table, the configuration models, the validators and the startup services.

The generator writes a public method `PopulateServiceCollection(IServiceCollection)` on the module.
It registers the module and every module it imports. The host project's `Program.cs` calls it on the
application module. [Hosts](/guide/hosts) shows `Program.cs` for each host.

The attribute class that the build writes is public and is in the module's namespace. Its name is
the module's name with `Attribute` appended. `TodosLibrary` gets `TodosLibraryAttribute`, written
`[TodosLibrary]`.

`dotnet new hardened-library` writes a project whose module holds services and no host.
[Project templates](/guide/project-templates) covers it.

## Import a module

The attributes that choose the host and add framework features are module attributes too.
`[KestrelRuntime]` is the attribute of the module `KestrelRuntime`. `[HardenedWebModule]` is the
attribute of the module `HardenedWebModule`.

Imports are transitive. `[KestrelRuntime]` imports `[HardenedWebModule]`. An application that names
only `[KestrelRuntime]` therefore serves `/health/live`, which the web module registers.

Imports are followed depth first, in the order they are written. A module that is reached more than
once is applied once. The `Equals` the generator writes for a `[HardenedModule]` class compares the
type alone. When such a module is imported twice with different property values, the first import
reached is applied and the other is dropped. The build reports nothing. A module class can override
`Equals`. `HardenedOpenApiUi` compares its `Path`, so two imports with different paths both apply.

Modules are applied in the reverse of the order they are first reached. The application module is
reached first, so it is applied last. A registration in the application module therefore wins a
single resolve over an imported module's registration of the same service type. When two modules
that the application imports register the same service type, the one written first on the
application wins a single resolve. [Registering services](/guide/services) covers replacing a
registration.

## Module properties

A public property with a setter on a module becomes a property of the module's attribute. The
template's application sets two properties of an imported framework module this way:
`[HardenedOpenApiUi(Title = "Todos", Environments = "development")]`.

`TodosLibrary` in `src/Todos/TodosLibrary.cs` declares an `Owner` property:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public string Owner { get; set; } = "nobody";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
        services.AddSingleton(new TodoOwner(Owner));
    }
}

public record TodoOwner(string Name);
```

`Application` in `src/Todos.Host/Application.cs` sets it:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[TodosLibrary(Owner = "ian")]
public partial class Application;
```

The attribute creates the module with `new` and copies each property it carries onto it. The
module's `ConfigureServices` runs on that instance, so it reads the value the importer set. A handler
in `src/Todos/OwnerController.cs` receives the `TodoOwner` that `ConfigureServices` registered:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class OwnerController
{
    [Get("/owner")]
    public string Owner([FromServices] TodoOwner owner) => owner.Name;
}
```

```http
GET /todos/owner

HTTP/1.1 200 OK
Content-Type: application/json

"ian"
```

A property of a reference type that the attribute leaves unset keeps its initializer. Under
`[TodosLibrary]` with no arguments, `Owner` is `nobody`.

::: warning
A property of a value type, such as `int` or `bool`, is copied whether the attribute sets it or not.
In every application that imports the module without setting the property, the module gets the
type's default, `0` or `false`, whatever the initializer says.
:::

## Register services in code

A class with a lifetime attribute registers itself. [Registering services](/guide/services) covers
the attributes.

A module that computes a registration implements `IServiceCollectionConfiguration`, from the
namespace `DependencyModules.Runtime.Interfaces`. Its `ConfigureServices(IServiceCollection)`
receives the application's service collection. `TodosLibrary` above does this. The interface has a
second method, `ConfigureDecorators(IServiceCollection)`, which does nothing unless the module
declares it.

`IEnvironmentServiceCollectionConfiguration`, in the same namespace, has one method,
`ConfigureServices(IServiceCollection, IModuleEnvironment)`. The environment that this method
receives is the one `[IfEnvironment]` reads. Its `EnvironmentName` is the Hardened environment's
name.

A module that implements both interfaces gets both calls. The methods run in this order:

| Method | Interface | Runs |
|---|---|---|
| `ConfigureServices(IServiceCollection)` | `IServiceCollectionConfiguration` | After the module's attribute registrations |
| `ConfigureServices(IServiceCollection, IModuleEnvironment)` | `IEnvironmentServiceCollectionConfiguration` | Next, on a module that implements both |
| `ConfigureDecorators(IServiceCollection)` | `IServiceCollectionConfiguration` | After every module's `ConfigureServices` and the declared decorators |

`ConfigureServices` can therefore read or replace the module's attribute registrations.

With the code below, the application logs at `Debug` in `development`, which is the environment when
`HARDENED_ENVIRONMENT` is unset. It does not log at `Debug` in `production`.

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[TodosLibrary]
public partial class Application : IEnvironmentServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services, IModuleEnvironment environment)
    {
        if (environment.EnvironmentName == "development")
        {
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
        }
    }
}
```

A class that should exist in some environments only takes `[IfEnvironment]` instead of code.
[Registering services](/guide/services) covers it. [Environments](/guide/environments) covers the
environment's name.

## Run code at startup

`IStartupService` is in the namespace `Hardened.Shared.Runtime.Application`. Its one method is
`Task<bool> Startup(IServiceProvider rootProvider)`. Each registered startup service runs once,
after the service provider is built and before the host takes its first request.

`TodoCountLogger` in `src/Todos/TodoCountLogger.cs` logs how many todos the store holds:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.Logging;

namespace Todos;

[SingletonService]
public class TodoCountLogger(ITodoStore store, ILogger<TodoCountLogger> logger) : IStartupService
{
    public async Task<bool> Startup(IServiceProvider rootProvider)
    {
        var todos = await store.All();

        logger.LogInformation("Starting with {Count} todos", todos.Count);

        return true;
    }
}
```

In the output of `dotnet run --project src/Todos.Host`, its line comes before `Listening on`:

```console
info: Todos.TodoCountLogger[0] Starting with 2 todos
info: Todos.Host[0] Listening on http://localhost:5080
```

`[SingletonService]` registers `TodoCountLogger` as `IStartupService`, the first interface it
declares. A class that declares another interface first is registered as that interface. Its
`Startup` never runs. `[SingletonService(As = typeof(IStartupService))]` registers such a class as a
startup service.

`Startup` receives the root service provider. A startup service is resolved from the container, so
its constructor takes services too.

The host starts every startup service, then waits for all of them. They run at the same time, so one
must not depend on another having finished. Hardened registers startup services of its own, for
CORS, authentication, authorization and routes registered at startup. The application's startup
services run at the same time as these.

The host does not use the `bool` that `Startup` returns:

| `Startup` | The application |
|---|---|
| Returns `true` | Starts |
| Returns `false` | Starts and answers requests |
| Throws | Does not start. The exception ends the process once the other startup services finish |

When a startup service throws, the call that runs the startup services throws. On Kestrel,
`StartAsync` throws the exception. On ASP.NET Core, `UseHardened` throws an `AggregateException`
that wraps it.

In a `[HardenedTest]`, the startup services run before the test method. [Hosts](/guide/hosts) covers
when each host runs the startup services. A startup service can register a filter for every handler.
[The execution pipeline](/guide/execution-pipeline) covers it.

## Next

| Page | Covers |
|---|---|
| [Registering services](/guide/services) | The lifetime attributes, service types and `[IfEnvironment]` |
| [Configuration](/guide/configuration) | The configuration models a module carries |
| [Environments](/guide/environments) | The environment a module reads |
| [Hosts](/guide/hosts) | Each host's `Program.cs`, and when it runs the startup services |
