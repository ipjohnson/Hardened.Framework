# Configuration

`[ConfigurationModel]` on a `partial` class makes it a configuration model. The generator writes an
interface named `I` followed by the class name, with a read-only property for each field.

The examples extend the `hardened-web` template's `Todos` application, which
[Getting started](/guide/getting-started) describes. The model is `src/Todos/TodoListOptions.cs`:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Todos;

[ConfigurationModel]
public partial class TodoListOptions
{
    [FromEnvironmentVariable("TODOS_PAGE_SIZE")]
    private int _pageSize = 10;
}
```

`[FromEnvironmentVariable("TODOS_PAGE_SIZE")]` fills the field from that environment variable. The
field's initializer is the default. The generator registers `IOptions<ITodoListOptions>` in the
container. The `All` handler in `src/Todos/TodoController.cs` reads the page size from it:

```csharp
using Hardened.Web.Runtime.Attributes;
using Microsoft.Extensions.Options;

namespace Todos;

public class TodoController(IOptions<ITodoListOptions> options)
{
    [Operation("listTodos")]
    [Get("/")]
    public async Task<IReadOnlyList<Todo>> All(ITodoStore store)
    {
        var todos = await store.All();

        return todos.Take(options.Value.PageSize).ToList();
    }
}
```

Run the host with the variable set:

```bash
TODOS_PAGE_SIZE=1 dotnet run --project src/Todos.Host
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true}]
```

Without the variable, `PageSize` is 10:

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

## Declare a model

`[ConfigurationModel]` and `[FromEnvironmentVariable]` are in the namespace
`Hardened.Shared.Runtime.Attributes`, in the package `Hardened.Shared.Runtime`. The generator is in
`Hardened.Library.SourceGenerator`. The template's projects reference it.

For the model above, the generator writes the following:

| Declared | Generated |
|---|---|
| The class `TodoListOptions` | The interface `ITodoListOptions`, in the same namespace. `TodoListOptions` implements it |
| The field `_pageSize` | The property `PageSize`: `get` on the interface, `get` and `set` on the class |
| The initializer `10` | The default, kept when the variable is unset or empty |
| `[FromEnvironmentVariable("TODOS_PAGE_SIZE")]` | A read of `TODOS_PAGE_SIZE`, converted to `int`, when the model is built |
| A field marked `[HideConfigurationField]` | No property, on the class or on the interface |
| The module in the same project, `TodosLibrary` | Registrations of `IOptions<ITodoListOptions>` and of the configuration package that builds the model |

`[HideConfigurationField]` is in `Hardened.Shared.Runtime.Attributes` too. Every field without it
becomes a property, whatever its accessibility. The property name is the field name with its
leading underscores removed and its first letter in upper case:

| Field | Property |
|---|---|
| `_pageSize` | `PageSize` |
| `__doubled` | `Doubled` |
| `pageSize` | `PageSize` |

The template sets `EmitCompilerGeneratedFiles`. With it, the generated code is in
`obj/Debug/net8.0/generated/Hardened.Library.SourceGenerator/Hardened.Library.SourceGenerator.LibrarySourceGenerator/`:

| File | Holds |
|---|---|
| `ConfigurationModels_TodoListOptions.Properties.cs` | The interface and the properties |
| `TodosLibrary.Configuration.cs` | The read of `TODOS_PAGE_SIZE` and the registrations |

The build refuses these declarations:

| Declaration | Build error |
|---|---|
| The class without `partial` | `CS0260` |
| An `internal` class | `CS0262`. The generated half is `public` |
| A `readonly` field | `CS0191`, in the generated property's setter |
| A `const` field | `CS0131`, in the generated property's setter |

## Environment variables

The generated code reads the variable through `IHardenedEnvironment.Value`. It passes the field's
current value as the default. The environment converts the text to the field's type. A field without
`[FromEnvironmentVariable]` keeps its initializer until an amendment changes it.

A value given to the environment itself wins over the process's variable. With
`new EnvironmentImpl(arguments: args, environmentValues: new Dictionary<string, string> { ["TODOS_PAGE_SIZE"] = "1" })`
in `Program.cs` and `TODOS_PAGE_SIZE=5` in the process, `GET /todos` returns one todo. In a test, only
the values the test declares are read. The process's variables are not read.
[Environments](/guide/environments) covers the environment's own values and the environment in a
test. It lists the types a value converts to and the exceptions for other types.

The model is built the first time something resolves it. For `TodoController`, that is the first
request to it, not startup. A value that does not convert throws when the model is built.
`TODOS_PAGE_SIZE=ten` throws `FormatException`. With the model in the constructor, every request to
the controller answers 500:

```http
GET /todos

HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{"type":"ServerError","message":"The server could not complete this request.","details":""}
```

The log shows a `HandlerCreationException` around the `FormatException`:

```text
fail: Hardened.Requests.Runtime.Logging.RequestLogger[0] GET /todos request failed Hardened.Requests.Runtime.Filters.HandlerCreationException: GET /todos could not construct its handler Todos.TodoController: The input string 'ten' was not in a correct format.  ---> System.FormatException: The input string 'ten' was not in a correct format.
```

A model that fails to build is not stored. Each request builds it again and fails again.

## Read a model

Take `IOptions<ITodoListOptions>` in a constructor, as `TodoController` does. The container builds
the controller for each request.

`GetConfiguration<ITodoListOptions>()` on `IConfigurationManager` also returns the model. The
configuration manager is in `Hardened.Shared.Runtime.Configuration`. A handler can take it as a
parameter:

```csharp
using Hardened.Shared.Runtime.Configuration;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    public async Task<IReadOnlyList<Todo>> All(ITodoStore store, IConfigurationManager configuration)
    {
        var options = configuration.GetConfiguration<ITodoListOptions>();

        var todos = await store.All();

        return todos.Take(options.PageSize).ToList();
    }
}
```

Both ways return one instance for the whole application.

The container holds the model only as `IOptions<ITodoListOptions>`. `GetConfiguration<T>` takes the
interface. Other ways of asking for the model fail:

| Asks for | Result |
|---|---|
| `ITodoListOptions` in a constructor | Fails the request with "Unable to resolve service for type 'Todos.ITodoListOptions' while attempting to activate 'Todos.TodoController'." |
| `GetConfiguration<TodoListOptions>()`, with the class | Throws `Exception` with the message "TodoListOptions is not a registered configuration type" |
| `IOptionsMonitor<ITodoListOptions>` | Resolves. Reading its `CurrentValue` throws `MissingMethodException`: "Cannot dynamically create an instance of type 'Todos.ITodoListOptions'. Reason: Cannot create an instance of an interface." |
| A handler method parameter that names `ITodoListOptions` | Fails the build. [Limits](#limits) has the error |

## Models in other projects

Each class marked `[HardenedModule]` registers every configuration model in its own project. In the
template, `TodosLibrary` registers `TodoListOptions`. A model reaches the application when the
application imports a module from the model's project. [Modules](/guide/modules) covers importing.

For a model `SharedOptions` in a separate project, `Settings`, a handler can take
`IOptions<ISharedOptions>` as a method parameter. When nothing imports a module from `Settings`,
both ways of reading the model fail:

| Asks for | Result |
|---|---|
| `IOptions<ISharedOptions>` | Resolves. Reading its `Value` throws `MissingMethodException`: "Cannot dynamically create an instance of type 'Settings.ISharedOptions'. Reason: Cannot create an instance of an interface." |
| `GetConfiguration<ISharedOptions>()` | Throws `Exception` with the message "ISharedOptions is not a registered configuration type" |

## Amend a model

`AppConfig` collects amendments. It is in `Hardened.Shared.Runtime.Configuration`. Register it as an
`IConfigurationPackage`. Here the application module registers it in `ConfigureServices`. The code is
in `src/Todos.Host/ApplicationConfiguration.cs`, a second file of the `partial class Application`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend((TodoListOptions options) => options.PageSize = 1, "production");

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

`ConfigureServices` comes from `IServiceCollectionConfiguration`. [Modules](/guide/modules) covers
it.

`Amend` takes an action on the model's class, `TodoListOptions`. The interface's properties have no
setter. The second argument names the environment the amendment runs in. Without it, the amendment
runs in every environment:

```csharp
config.Amend((TodoListOptions options) => options.PageSize = 1);
```

The amendments of every registered configuration package run in registration order. Within one
package, they run in the order of the `Amend` calls. The application module is applied after the
modules it imports. Its amendments run after a library's. When both amend the same property, the
application's value stays. [Modules](/guide/modules) covers the order.

Amendments run after the environment variables are read. An amendment wins over a variable.
Amendments run once, when the model is built.

`Amend` also changes the configuration types that framework modules register. An amendment in the
host's `Application` does not run in the template's tests. The tests build `TodosLibrary`.

## Amend in one environment

The name in the second argument must match the environment's name exactly, including case. With the
`production` amendment above:

| `HARDENED_ENVIRONMENT` | `GET /todos` returned |
|---|---|
| unset (`development`) | two todos |
| `production` | one todo |
| `Production` | two todos |
| `staging` | two todos |

::: warning
An amendment scoped to `production` does not run when `HARDENED_ENVIRONMENT` is `Production`.
Nothing reports it. `Matches`, `[IfEnvironment]` and `[IfNotEnvironment]` ignore case. The rest of
the application treats `Production` as `production`.
:::

Another overload of `Amend` takes a function. The function receives the environment as well as the
model. The environment's `Matches` takes several names:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend(
            (IHardenedEnvironment environment, TodoListOptions options) =>
            {
                if (environment.Matches("production", "staging"))
                {
                    options.PageSize = 1;
                }

                return options;
            }
        );

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

The function must return the object it was given. An object it returns in its place is discarded.
The changes that the amendments after it make are discarded too.

## Replace a model

`ProvideValue<ITodoListOptions, TodoListOptions>` supplies the whole model from a function that
receives the environment. In `src/Todos.Host/ApplicationConfiguration.cs`, this call takes the place
of the `Amend` line:

```csharp
config.ProvideValue<ITodoListOptions, TodoListOptions>(
    environment => new TodoListOptions { PageSize = environment.Value("PAGE_SIZE", 20) }
);
```

For each interface, the last registered package that provides it wins. An application's
`ProvideValue` replaces the model a library's module registers. A supplied model's
`[FromEnvironmentVariable]` fields are not read. The function builds all of it. With the call above:

| Variable set | `GET /todos` returned |
|---|---|
| none | two todos |
| `PAGE_SIZE=1` | one todo |
| `TODOS_PAGE_SIZE=1` | two todos |

Amendments still run on a supplied model.

## Limits

A handler method parameter that names the interface of a model in the same project fails the build
with `CS0400`. The generated handler names the interface without its namespace. Take the model in
the constructor, as `TodoController` does, or use the configuration manager. With
`All(ITodoStore store, IOptions<ITodoListOptions> options)` in `src/Todos/TodoController.cs`, the
build reports:

```text
src/Todos/obj/Debug/net8.0/generated/Hardened.Web.SourceGenerator/Hardened.Web.SourceGenerator.WebLibrarySourceGenerator/TodoController_All_1585.cs(67,74): error CS0400: The type or namespace name 'ITodoListOptions' could not be found in the global namespace (are you missing an assembly reference?)
```

Models read environment variables and the environment's own values. They do not read
`appsettings.json` or `IConfiguration`.

Nothing rebuilds a model while the application runs. A variable changed later is not read.

## Next

- [Environments](/guide/environments): the environment's name, how a value is read and converted,
  and the environment in tests
- [Modules](/guide/modules): modules, importing them, and `ConfigureServices`
- [Writing a test](/guide/testing): `[EnvironmentValue]` and the environment a test runs in
