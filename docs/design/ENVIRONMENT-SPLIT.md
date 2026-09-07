# An application has two environments, and they disagree

Found while making the template's reference page development-only. The gate worked in unit tests
and did nothing in a generated application, and the reason is not the gate.

## What happens

A Hardened application running with `HARDENED_ENVIRONMENT=development`, registered exactly the way
`guide/getting-started` documents, reports this from inside module application:

```
[probe] module application sees EnvironmentName = 'Production'
        (type DependencyModules.Runtime.ModuleEnvironment+ProcessModuleEnvironment)
```

There are two environments:

| | Read from | Default | Decides |
|---|---|---|---|
| `IHardenedEnvironment` | `HARDENED_ENVIRONMENT` | `development` | configuration models, `environment.Matches(...)`, application code |
| `IModuleEnvironment` | `ASPNETCORE_ENVIRONMENT` | `Production` | `[IfEnvironment]`, `IEnvironmentServiceCollectionConfiguration` — *what gets registered at all* |

Different variables, different defaults, opposite answers out of the box. A fresh application is
simultaneously `development` to its own code and `Production` to everything that decides which
services exist.

`guide/environments` says the opposite, in as many words:

> `IHardenedEnvironment` implements DependencyModules' `IModuleEnvironment` for exactly this
> reason — there is one environment, and the conditional registrations, the configuration models
> and the application code all see the same answer.

The interface relationship is real. The registration is what breaks it.

## Why

`EnvironmentImpl` implements both interfaces, but the documented host registers it under one:

```csharp
builder.Services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
```

When modules are applied, DependencyModules calls `FindModuleEnvironment` over the service
collection, looks for `IModuleEnvironment`, does not find one, and falls back to its own
`ProcessModuleEnvironment`.

Registering it as an instance rather than a factory does not help — the lookup is by service type.

## What it costs

Anything evaluated while modules are applied is evaluated against the wrong environment:

- `[IfEnvironment]`, `[IfNotEnvironment]`, `[IfEnvironmentValue]`, `[IfNotEnvironmentValue]`
- `IEnvironmentServiceCollectionConfiguration`

`guide/services` documents this pair as the way to swap an implementation per environment:

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender : IEmailSender { }

[SingletonService(As = typeof(IEmailSender))]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender { }
```

Under the documented host shape, a developer running locally gets `SmtpEmailSender`.

**And the tests pass.** `TestApplication` calls `ConfigureModule(environment, serviceCollection)`,
which passes the Hardened environment explicitly, so under `[HardenedTest]` the two environments
are the same object and `[EnvironmentName("production")]` behaves exactly as documented. Green
tests, different production behaviour — the same shape as `IStartupService` not running under the
ASP.NET host.

## The fix

`AddHardenedEnvironment` registers the one instance under both service types:

```csharp
services.AddHardenedEnvironment(args);
```

That is now the documented line, in the README and in the `hardened-web` template. It replaces:

```csharp
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
```

which publishes only the type application code reads and leaves the module system to guess.

Verified end to end: with the old line, `guide/services`' own example hands a developer running
under `HARDENED_ENVIRONMENT=development` the SMTP sender. With the new one it hands them the
console sender, and the same application under `production` gets SMTP.

| Registration | `HARDENED_ENVIRONMENT=development` | `=production` |
|---|---|---|
| `AddTransient<IHardenedEnvironment>` | **Smtp (production)** | Smtp (production) |
| `AddHardenedEnvironment` | Console (development) | Smtp (production) |

`ASPNETCORE_ENVIRONMENT` no longer decides anything: it was only ever read by the fallback, and
with the environment always registered the fallback is unreachable. `HARDENED_ENVIRONMENT` is the
one variable, which is the right answer for a framework that also runs on Kestrel, on Lambda and
in a console.

## Still open

- **`guide/environments` and `guide/getting-started` need the new line.** They live in the other
  repository, so this change cannot reach them. The page currently promises one environment and
  shows the registration that produces two.
- **Nothing stops a host forgetting it.** `AddHardenedEnvironment` makes the right thing one call,
  but an application that registers `IHardenedEnvironment` by hand still gets the split silently.
  A guard - the module system reporting when it fell back rather than falling back quietly - would
  close it.
- **The defaults still differ.** `development` against `Production` is what turns a missed
  registration into a wrong answer rather than a harmless one. Unreachable now, and still a sharp
  edge for anyone composing a container by hand.

## Two smaller things found alongside

**A spec-first reference page cannot be gated.** `[HardenedOpenApiUi]` now takes `Environments`,
but a spec-first application installs its page through `UiUrl` metadata on the contract item
instead, and that path has no environment story. The template's page is development-only for
`--contract code` and served everywhere for `openapi` and `smithy`.

**`UiUrl` and `PublishUrl` on `HardenedSmithyModel` are silently dropped.** The targets synthesise
the `HardenedSmithyAst` item from the compiled model without copying metadata across
(`Hardened.Smithy.SourceGenerator.targets:131`), and only the AST item is read
(`:228`). A Smithy application setting either gets no served document and no reference page, with
no diagnostic. The template omits both and says why.
