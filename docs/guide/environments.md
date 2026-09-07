# Environments

`HARDENED_ENVIRONMENT` names where the application is running. Registrations, configuration
amenders and the log level all read it.

```
HARDENED_ENVIRONMENT=production dotnet run
```

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender : IEmailSender { }

[SingletonService(As = typeof(IEmailSender))]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender { }
```

Unset, the name is `development`. A test runs as `test` unless it
[says otherwise](/guide/testing#environments-in-tests).

## Registering one

A host registers the environment before handing the collection to the module:

```csharp
services.AddHardenedEnvironment(args);                             // name from HARDENED_ENVIRONMENT
services.AddHardenedEnvironment(new EnvironmentImpl("staging"));   // or an explicit one
```

::: danger Use AddHardenedEnvironment, not AddSingleton
The environment has to be reachable as both `IHardenedEnvironment` and `IModuleEnvironment`.
`AddSingleton(environment)` registers the first only. The module system then falls back to its
own default, which reads `ASPNETCORE_ENVIRONMENT` and answers `Production`, so `[IfEnvironment]`
sees `Production` while the rest of the application reads `HARDENED_ENVIRONMENT` and says
`development`. Nothing fails. An environment-gated service is quietly missing.
:::

A self-hosting entry point, console or Lambda, gets a generated constructor that does it for you,
and another that takes one:

```csharp
var application = new Application();                          // EnvironmentImpl with defaults
var application = new Application(new EnvironmentImpl("qa")); // explicit
```

`EnvironmentImpl` takes everything by optional argument:

```csharp
new EnvironmentImpl(
    name: "staging",
    environmentValues: new Dictionary<string, string> { ["FEATURE_X"] = "on" },
    arguments: args,
    customData: new Dictionary<string, object> { ["tenant"] = tenant });
```

## The name

Two names carry framework behaviour. `development` and `test` default the log level to `Debug`,
and every other name defaults to `Information`. Beyond that, names are yours. The helpers compare
them case-insensitively:

```csharp
if (environment.Matches("production", "staging")) { /* … */ }

if (environment.MatchesVariable("FEATURE_X", "on")) { /* … */ }
```

`[IfEnvironment]`, `[IfNotEnvironment]`, `[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` are
evaluated when the modules are applied, against this same environment. See
[Conditional registration](/guide/services#conditional-registration).

## Reading values

```csharp
public interface IHardenedEnvironment : IModuleEnvironment {
    string Name { get; }

    IReadOnlyList<string> Arguments { get; }

    T? Value<T>(string name, T? defaultValue = default);

    T? CustomData<T>(string name, T? defaultValue = default);
}
```

`Value<T>` looks in the dictionary the environment was constructed with, then in the process
environment, and converts to `T`:

```csharp
var region  = environment.Value("AWS_REGION", "us-west-2");
var timeout = environment.Value("TIMEOUT_SECONDS", 30);
var debug   = environment.Value("VERBOSE", false);
```

The dictionary wins over the process, which lets a test set a value without touching the machine
it runs on.

A variable the application depends on belongs in a [configuration model](/guide/configuration),
declared once with a typed default. `Value` is for the places that have no model: inside a
model's own construction, or in a condition that runs before configuration exists.

`CustomData<T>` carries objects rather than strings and is not backed by the process environment.
It is for values a host has in hand and cannot put in a variable, such as a resolved tenant or an
already-constructed client:

```csharp
var tenant = environment.CustomData<Tenant>("tenant");
```

## Log level

The default logging setup derives its minimum level from the environment:

1. `Information`, or `Debug` when the environment is named `development` or `test`.
2. Overridden by the `LOG_LEVEL` variable if it parses as a `LogLevel`.

```
LOG_LEVEL=Warning
```

`Microsoft` and `System` are filtered to `Warning` regardless. To take over entirely, declare
`ConfigureLogging` on a self-hosting entry point; see
[Self-hosting entry points](/guide/modules#self-hosting-entry-points).

## Next

- [Configuration](/guide/configuration): typed models over the variables
- [Registering services](/guide/services#conditional-registration): registrations gated on the name
- [Environments in tests](/guide/testing#environments-in-tests): naming the environment a test runs as
