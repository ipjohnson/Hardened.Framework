# Environments

`IHardenedEnvironment` is the environment the application runs in: a name, the arguments it was
given, values it reads by name, and custom data. A handler takes it as a parameter, like any
service:

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class EnvironmentController
{
    [Get("/environment")]
    public string Name(IHardenedEnvironment environment) => environment.Name;
}
```

The examples on this page are in a `hardened-web` template application named `Todos`. Its library
module, `TodosLibrary`, has `[BasePath("/todos")]`, so the handler above answers
`/todos/environment`.

The name comes from the environment variable `HARDENED_ENVIRONMENT`:

```bash
HARDENED_ENVIRONMENT=production dotnet run --project src/Todos.Host
```

```http
GET /todos/environment

HTTP/1.1 200 OK
Content-Type: application/json

"production"
```

The name is `development` when the variable is unset:

```http
GET /todos/environment

HTTP/1.1 200 OK
Content-Type: application/json

"development"
```

The interface is in the `Hardened.Shared.Runtime` package, in the namespace
`Hardened.Shared.Runtime.Application`.

## Set the name

The name keeps the case it is given. `HARDENED_ENVIRONMENT=Development` gives the name
`Development`. A `HARDENED_ENVIRONMENT` set to an empty string gives an empty name, not
`development`.

`ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT` do not change the name. A command-line argument
does not change it either. After `dotnet run --project src/Todos.Host -- --environment production`,
the name is `development`. `Arguments` holds `--environment` and `production`.

Four features read the name:

| Feature | Page |
|---|---|
| `Matches`, in application code | [Match the name](#match-the-name) |
| `[IfEnvironment]` and `[IfNotEnvironment]` on a registered class | [Registering services](/guide/services) |
| `AppConfig.Amend` given an environment name | [Configuration](/guide/configuration) |
| `[HardenedOpenApiUi(Environments = ...)]` | [The OpenAPI document](/guide/openapi-document) |

## Register the environment

`AddHardenedEnvironment` registers the environment. It is an extension method on
`IServiceCollection`, in `Hardened.Shared.Runtime.Application`. The template's `Program.cs` calls it
before `PopulateServiceCollection`.

The method registers one instance under two service types: `IHardenedEnvironment`, and
`IModuleEnvironment` from DependencyModules. `IModuleEnvironment` is in the namespace
`DependencyModules.Runtime.Interfaces`. Through either service type, the environment gives the same
name and values. Application code and configuration models read `IHardenedEnvironment`. The module
system reads `IModuleEnvironment` while `PopulateServiceCollection` runs, to decide the
registrations marked `[IfEnvironment]` and `[IfNotEnvironment]`.
[Registering services](/guide/services) describes both attributes.

An application that registers no environment fails as it starts, with an
`InvalidOperationException`:

```text
Unable to resolve service for type 'Hardened.Shared.Runtime.Application.IHardenedEnvironment' while attempting to activate 'Hardened.Shared.Runtime.Configuration.ConfigurationManager'.
```

::: warning
Register the environment with `AddHardenedEnvironment`, before `PopulateServiceCollection`. Any
other registration leaves `IModuleEnvironment` unregistered while `PopulateServiceCollection` runs.
`AddSingleton<IHardenedEnvironment>(environment)`, `AddTransient<IHardenedEnvironment>(...)` and a
call to `AddHardenedEnvironment` after `PopulateServiceCollection` all do this.

The application then runs with two environments. The module system uses an environment of its own.
Its name comes from `ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`. It is `Production` when
neither is set. `[IfEnvironment]`, `[IfNotEnvironment]` and the reference page at `/docs` follow
that environment. Application code follows `HARDENED_ENVIRONMENT`. Nothing reports the difference.
:::

Each host registers the environment in one place:

| Host | Registered by |
|---|---|
| Kestrel, ASP.NET Core, AWS Lambda, Google Cloud Run | The template's `Program.cs`, with `new EnvironmentImpl(arguments: args)`, then `AddHardenedEnvironment(environment)` before `PopulateServiceCollection` |
| Azure Functions | The template's `Program.cs` passes the environment to `UseHardened<Application>(environment)`. `UseHardened` calls `AddHardenedEnvironment` before `PopulateServiceCollection`. Without the argument, it builds `new EnvironmentImpl(arguments: Environment.GetCommandLineArgs())` |
| Google Cloud Functions | `HardenedFunctionsStartup<TApplication>`, which registers `new EnvironmentImpl(arguments: Environment.GetCommandLineArgs())` |
| A test | The test runner, described in [Tests](#tests) |

`AddHardenedEnvironment` has two overloads:

| Call | Registers |
|---|---|
| `AddHardenedEnvironment(arguments)` | A new `EnvironmentImpl` named from `HARDENED_ENVIRONMENT`, with `arguments` as its `Arguments`. `arguments` is optional |
| `AddHardenedEnvironment(environment)` | The environment it is given |

Either line below can replace `services.AddHardenedEnvironment(environment);` in the template's
`Program.cs`. That file has `using Hardened.Shared.Runtime.Application;`. The first line calls
`AddHardenedEnvironment(arguments)`:

```csharp
services.AddHardenedEnvironment(args);
```

The second calls `AddHardenedEnvironment(environment)`:

```csharp
services.AddHardenedEnvironment(new EnvironmentImpl("staging"));
```

`EnvironmentImpl` is the implementation every host registers. It is in
`Hardened.Shared.Runtime.Application`. Every constructor parameter is optional:

| Parameter | Type | When omitted | Sets |
|---|---|---|---|
| `name` | `string?` | `HARDENED_ENVIRONMENT`, or `development` when it is unset | `Name` |
| `environmentValues` | `IDictionary<string, string>?` | No values | The values `Value` reads before the process's environment variables |
| `arguments` | `IReadOnlyList<string>?` | Empty | `Arguments` |
| `customData` | `IDictionary<string, object>?` | No custom data | The objects `CustomData` returns |

This line can replace `var environment = new EnvironmentImpl(arguments: args);` in the template's
`Program.cs`:

```csharp
var environment = new EnvironmentImpl(
    name: "staging",
    environmentValues: new Dictionary<string, string> { ["FEATURE_EXPORT"] = "true" },
    arguments: args,
    customData: new Dictionary<string, object> { ["startedAt"] = DateTimeOffset.UtcNow }
);
```

## Match the name

`Matches` returns true when the name equals any of the names passed to it, ignoring case.
`MatchesVariable(variable, value)` returns true when the value of `variable` equals `value`,
ignoring case. An unset variable reads as an empty string. Both are extension methods in
`Hardened.Shared.Runtime.Application`.

The template's `Program.cs` uses `Matches`:

```csharp
if (environment.Matches("development"))
{
    logger.LogInformation("Browse http://localhost:{Port}/docs to access your API.", port);
}
```

## Read values

`Value<T>(name, defaultValue)` looks for `name` in the values the environment was given, then in
the process's environment variables. It returns `defaultValue` when it finds no value. An empty
value counts as unset, at each step.

This handler reads `FEATURE_EXPORT`:

```csharp
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class FeatureController
{
    [Get("/features/export")]
    public bool Export(IHardenedEnvironment environment) => environment.Value("FEATURE_EXPORT", false);
}
```

```bash
FEATURE_EXPORT=true dotnet run --project src/Todos.Host
```

```http
GET /todos/features/export

HTTP/1.1 200 OK
Content-Type: application/json

true
```

Without the variable, the body is `false`.

`Value` returns a `string` as it is. It converts any other type with `Convert.ChangeType`, using the
process's current culture:

| `T` | Converts | When it cannot |
|---|---|---|
| `string` | Returned as it is | Always succeeds |
| `int`, `long`, `double` | Parsed with the current culture | `FormatException` |
| `bool` | `true` or `false` | `FormatException`. `1` throws |
| `DateTime` | Parsed with the current culture | `FormatException` |
| An enum, `TimeSpan`, `Guid`, `Uri` | Never | `InvalidCastException` |
| A nullable type, such as `int?` | Never | `InvalidCastException` |

For a type that cannot convert, `Value` throws only when a value is present. With no value, `Value`
returns `defaultValue` without a conversion. Under the `de-DE` culture, `1.5` reads as the `double`
`15`.

A configuration model reads its `[FromEnvironmentVariable]` fields through `Value`. The same rules
apply to those fields. [Configuration](/guide/configuration) describes configuration models.

`Arguments` holds the arguments the environment was given. In the template, they are `args`.
Nothing in the framework reads them.

## Read custom data

`CustomData<T>(name, defaultValue)` returns the object stored under `name` in the environment's
custom data, cast to `T`. It returns `defaultValue` when there is none. It throws
`InvalidCastException` when the object is of another type. Custom data comes only from the
`customData` argument. Nothing reads it from the process.

With the environment built in [Register the environment](#register-the-environment), this line in
`Program.cs` reads `startedAt`:

```csharp
var startedAt = environment.CustomData<DateTimeOffset>("startedAt");
```

## Tests

A test's environment is named `test`. In a test, `Value` reads only the values the test declares.
It does not read the process's environment variables. `Arguments` is empty. `CustomData` returns
`defaultValue`.

The test runner registers the environment under both service types before it applies the modules.
`[IfEnvironment]` and `[IfNotEnvironment]` therefore see `test`.

`[EnvironmentName]` changes the name of a test's environment. `[EnvironmentValue]` gives the test's
environment a value. [Writing a test](/guide/testing) covers both.

## Next

- [Configuration](/guide/configuration): typed models over environment variables, and amendments
  for one environment
- [Registering services](/guide/services): registrations for one environment with
  `[IfEnvironment]` and `[IfNotEnvironment]`
- [Writing a test](/guide/testing): `[EnvironmentName]` and `[EnvironmentValue]`
- [Hosts](/guide/hosts): each host's `Program.cs`
