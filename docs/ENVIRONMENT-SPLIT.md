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

## The fix an application can apply today

```csharp
var environment = new EnvironmentImpl(arguments: args);

services.AddSingleton<IHardenedEnvironment>(environment);
services.AddSingleton<IModuleEnvironment>(environment);
```

Verified: module application then reports `development`, and the gated page appears in development
and is absent in production. The `hardened-web` template does this, with the reason at the call
site.

## Worth deciding

One line in every host is a line every application can forget, and forgetting it is silent. Some
candidates, roughly in order of how much they remove the chance:

- **An extension that registers both.** `services.AddHardenedEnvironment(environment)` — one call,
  nothing to know, and a place to document why. Cheapest thing that ends the class of bug.
- **Have the runtime modules register it.** The framework does not register an environment on
  purpose, because only the application knows where its name comes from. That argument is about
  *which* environment, not about *how many interfaces* it lands under — the host could register
  the second one from the first.
- **Make the defaults agree.** `development` against `Production` is the part that turns a missing
  registration into a wrong answer rather than a lucky one.
- **Say it in `guide/environments`.** The page currently promises the opposite, so at minimum the
  promise needs to become true or become accurate.

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
