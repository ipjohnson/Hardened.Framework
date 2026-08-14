# Environments

`IHardenedEnvironment` is how an application asks where it is running and what it was started with.
It is the input to configuration, to conditional registration, and to the default log level.

```csharp
public interface IHardenedEnvironment : IModuleEnvironment {
    string Name { get; }

    IReadOnlyList<string> Arguments { get; }

    T? Value<T>(string name, T? defaultValue = default);

    T? CustomData<T>(string name, T? defaultValue = default);
}
```

## Registering one

The framework does not register an environment, because only the application knows where its name
and arguments come from. An ASP.NET Core host registers one before handing the collection to the
module:

```csharp
builder.Services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
```

A self-hosting entry point — console or Lambda — gets a generated constructor that does it for you,
and another that lets you pass one in:

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

## The environment name

With no name given, `EnvironmentImpl` reads `HARDENED_ENVIRONMENT`, and falls back to
`"development"`:

```
HARDENED_ENVIRONMENT=production dotnet run
```

Two names carry framework behaviour: `development` and `test` both default the log level to `Debug`
where every other name defaults to `Information`. Beyond that, names are yours — `Name` is compared
case-insensitively by the helpers:

```csharp
if (environment.Matches("production", "staging")) { /* … */ }

if (environment.MatchesVariable("FEATURE_X", "on")) { /* … */ }
```

::: tip The test environment is named for you
`[HardenedTest]` builds an environment called `test` unless the test says otherwise with
`[EnvironmentName("…")]`. See [Testing](/guide/testing#environments-in-tests).
:::

## Reading values

`Value<T>` looks in the dictionary the environment was constructed with, then in the process
environment, and converts to `T`:

```csharp
var region  = environment.Value("AWS_REGION", "us-west-2");
var timeout = environment.Value("TIMEOUT_SECONDS", 30);
var debug   = environment.Value("VERBOSE", false);
```

The explicit dictionary takes precedence over the process, which is what lets a test set a value
without touching the machine it runs on.

Most code should not call `Value` at all. A variable an application depends on belongs in a
[configuration model](/guide/configuration), where it is declared once, has a typed default, and can
be listed by reading the source. `Value` is for the places that have no model — inside a
configuration model's own construction, or in a condition that runs before configuration exists.

### Custom data

`CustomData<T>` carries objects rather than strings, and is not backed by the process environment. It
exists for values a host has in hand and cannot serialise into a variable — a resolved tenant, an
already-constructed client:

```csharp
var tenant = environment.CustomData<Tenant>("tenant");
```

## Environments during registration

Registration decisions do not need to read the environment at run time. `[IfEnvironment]` and
friends are evaluated when modules are applied, against this same environment:

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender : IEmailSender { }
```

`IHardenedEnvironment` implements DependencyModules' `IModuleEnvironment` for exactly this reason —
there is one environment, and the conditional registrations, the configuration models and the
application code all see the same answer.

## Log level

The default logging setup derives its minimum level from the environment:

1. `Information`, or `Debug` when the environment is named `development` or `test`.
2. Overridden by the `LOG_LEVEL` variable if it parses as a `LogLevel`.

```
LOG_LEVEL=Warning
```

`Microsoft` and `System` are filtered to `Warning` regardless, so raising the level does not bury
your own logs under the framework's. To take over entirely, declare `ConfigureLogging` on a
self-hosting entry point — see [Modules](/guide/modules#self-hosting-entry-points).
