# Project templates

The fastest correct start. One command installs them, one more writes a project that builds, tests
and serves.

```bash
dotnet new install Hardened.Templates
```

```bash
dotnet new hardened-web -n Greeter
cd Greeter
dotnet run --project src/Greeter.Host
```

```console
$ curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

The first run opens a reference page at `/docs`, so the API you just made is on screen rather than
one command away.

## Why start here rather than with packages

Hardened splits into runtime packages and generator packages, and **analyzers do not travel through
a package reference**. A project that references `Hardened.Web.Runtime` and nothing else compiles
cleanly and answers 404 to every route, because the generator that writes the routing table was
never there. Nothing fails; the application is just empty.

That is the single most common way to get a Hardened project wrong, and it is invisible. The
templates reference the right generators, pin every version in one place, and lay the projects out
so the host can be swapped without touching application code.

They are also the release gate: every combination below is generated, built, tested and — where it
serves HTTP — probed, before any release ships.

## The three templates

| Short name | For |
|---|---|
| `hardened-web` | An HTTP API. Kestrel, ASP.NET Core or AWS Lambda |
| `hardened-function` | An AWS Lambda function that is not an HTTP API |
| `hardened-library` | A reusable module other applications compose |

## hardened-web

```bash
dotnet new hardened-web -n Greeter [options]
```

| Option | Values | Default |
|---|---|---|
| `-ho, --host` | `kestrel`, `aspnet`, `aws-lambda` | `kestrel` |
| `-c, --contract` | `code`, `openapi`, `smithy` | `code` |
| `-rm, --response-model` | `standard`, `response`, `union` | `standard` |
| `--openapi-ui` | `true`, `false` | `true` |
| `--hardened-version` | any published version | the version the template shipped with |
| `--skip-restore` | `true`, `false` | `false` |

`--response-model` decides how the generated handlers declare what they can answer with, and the
sample is written to show the difference: the same three routes answer 404 and 409 in every mode,
while creating a todo answers 200 under `standard` and 201 under the other two. See
[Declared responses](/guide/responses).

`union` generates a `net11.0` project pinned to the .NET 11 SDK in `global.json`, because a C# 15
union needs the .NET 11 reference assemblies and not only the keyword. It cannot be combined with
`--host aws-lambda`, whose managed runtime is `net8.0` — the generated project refuses with
`HTPL001` rather than failing at deploy time.

### What you get

```
Greeter.sln
Directory.Packages.props        every version, in one place
src/Greeter/                    the implementation. Knows nothing about where it runs
src/Greeter.Host/               which runtime hosts it, and Program.cs
tests/Greeter.Tests/            tests, against the library rather than the host
```

That split is the design rather than a convention. Swapping `--host` changes only the middle
project — the library and the tests are identical whichever host you pick, which is why the tests
target the library. A test suite that named the host would be tied to a deployment target for no
reason.

### Choosing a host

`kestrel` is the default and the right answer unless something below applies. It serves HTTP through
Kestrel without the ASP.NET Core request pipeline.

`aspnet` when you need ASP.NET Core's own middleware, authentication and authorization, or its
hosting diagnostics — instrumentation packages that subscribe to the ASP.NET `DiagnosticSource`
names see nothing under Kestrel. See [the Kestrel host's trade-offs][kestrel] for the full list.

`aws-lambda` puts the application behind API Gateway. The host project loses its `Program.cs` —
the generator writes the entry point AWS invokes — and gains a `Greeter.Harness` project that runs
the same application locally over HTTP, so `dotnet run --project src/Greeter.Harness` still gives
you something to curl.

[kestrel]: https://github.com/ipjohnson/Hardened.Framework/blob/main/src/Web/Hardened.Web.Kestrel.Runtime/README.md

### Choosing a contract

`code` — the C# is the contract. Routes are attributes on plain methods, and the OpenAPI document is
generated from them.

`openapi` — an OpenAPI document is the contract. The models, the service interface, the routes and
the validation its constraints describe are all generated from it, and the implementation stops
compiling when the two disagree. That is the point rather than an inconvenience.

`smithy` — the same generated output from a Smithy model. Needs the [Smithy CLI][smithy] on `PATH`
at the pinned version; the build names the version it expects if yours differs, because two CLI
versions can produce different ASTs and therefore different generated C#.

[smithy]: https://smithy.io/2.0/guides/smithy-cli/index.html

With `--contract openapi` or `smithy` there are no route attributes anywhere in the project. Add an
operation to the contract and the build writes the model, the route and the validation, then stops
compiling until your service implements the new method.

### The reference page

`--openapi-ui` serves a page at `/docs` describing every operation, and the document behind it at
`/openapi.json`.

It is served **in the `development` environment only**. The page renders with a script from a CDN
and describes the whole API, neither of which a deployed service obviously wants. Widen it by
naming more environments:

```csharp
[HardenedOpenApiUi(Title = "Greeter", Environments = "development,staging")]
```

The environment is `HARDENED_ENVIRONMENT`, which defaults to `development` — see
[Environments](/guide/environments).

## hardened-function

An AWS Lambda function that is not an HTTP API.

```bash
dotnet new hardened-function -n OrderIntake [options]
```

| Option | Values | Default |
|---|---|---|
| `-tr, --trigger` | `invoke`, `sqs` | `invoke` |
| `-hv, --hardened-version` | any published version | the version the template shipped with |
| `--skip-restore` | `true`, `false` | `false` |

```
src/OrderIntake/                the function: its handler, models and services
tests/OrderIntake.Tests/        tests that invoke it the way Lambda does
```

There is no host project and no `Program.cs`. On Lambda the deployed artifact is the assembly and
the generator writes the entry point, so there is nothing for a host project to separate. Adding a
`Main` gives the assembly a second entry point and the build fails.

The handler is a plain class:

```csharp
public class OrderHandler(OrderLog log) {

    [HardenedFunction]
    public Task<OrderAccepted> Process(Order order) { ... }
}
```

With `--trigger sqs` the runtime unpacks the batch and calls the handler once per record. Returning
normally marks that record handled; throwing reports it as a batch item failure, so only the records
that failed are redelivered.

There is nothing to `dotnet run`. The tests are how the function is exercised locally — they invoke
it through the real pipeline, with no AWS account and nothing to deploy.

## hardened-library

A reusable module. It names no runtime, so one package serves Kestrel, ASP.NET Core and Lambda
alike.

```bash
dotnet new hardened-library -n Acme.Greeting
```

The build writes an attribute named after the module, and an application composes it exactly the way
it composes a runtime:

```csharp
[HardenedModule]
[KestrelRuntime]
[AcmeGreetingLibrary]     // everything the library registers
public partial class Application;
```

There is no `AddAcmeGreeting()` to call and no options object to thread through. The attribute is
generated from the module, so it cannot describe a service the library does not have.

To carry HTTP routes as well as services, add `[HardenedWebModule]` to the module class and
reference `Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator`.

## Versions

Every template writes a `Directory.Packages.props` with one version for all Hardened packages:

```xml
<HardenedVersion>0.11.0-rc1000</HardenedVersion>
```

That is the version the template package shipped with, stamped in at pack time rather than
hardcoded — so it never drifts from the release it belongs to. Generated code and the runtime it
targets ship together, so they move as a set. `--hardened-version` overrides it.

Templates do **not** update themselves. A newer release is a newer template package:

```bash
dotnet new install Hardened.Templates          # latest
dotnet new install Hardened.Templates::0.11.0-rc1000   # a specific one
```

Existing projects are unaffected — they keep the version in their own
`Directory.Packages.props` until you change it.

::: tip Hardened.Amz floats
The Lambda templates float their `Hardened.Amz` pin rather than tying it to `HardenedVersion`. The
two repositories release in sequence, so for a short window the framework is ahead — an exact pin
there would name a version that does not exist yet. The float resolves to the newest published
Hardened.Amz, which always works because the template pins the framework packages higher.
:::

::: warning Everything is prerelease
There is no stable release of anything Hardened yet. `dotnet new install Hardened.Templates` picks
the newest prerelease because there is no stable one to prefer, but adding a package by hand needs
`dotnet add package Hardened.Web.Runtime --prerelease` or it finds nothing.
:::

## Reading what the build wrote

`EmitCompilerGeneratedFiles` is already on in every template:

```
src/Greeter/obj/Debug/net8.0/generated/     one directory per generator
```

Build first. Reading that directory answers most "how does this work" questions faster than reading
the framework does — the routing table, the handler for each route, the parameter binding and the
module registration are all there, as ordinary C#.

## Each project explains itself

Every generated project carries two files:

- **`README.md`** — what the application is, how to run it, how the projects fit together, and how
  to add to it
- **`AGENTS.md`** — the invariants and traps, for whoever or whatever edits the code rather than
  reading it: which packages are load-bearing, why the module class stays empty, why the tests
  target the library

Both are written for the combination you chose. A spec-first project's `AGENTS.md` says *do not add
route attributes and do not look for them*; a code-first one explains the attributes.

## Next

- [Getting started](/guide/getting-started) — the same project assembled by hand
- [Modules](/guide/modules) — how `[HardenedModule]` composes
- [Testing](/guide/testing) — what `[HardenedTest]` boots
- [AWS](/aws/) — the Lambda runtimes in depth
