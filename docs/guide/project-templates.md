# Project templates

`dotnet new hardened-web` writes a solution that builds, tests and serves. Two more templates
write a Lambda function and a reusable library.

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos
cd Todos
dotnet run --project src/Todos.Host
```

```console
$ curl localhost:5080/todos
[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

The reference page is at `http://localhost:5080/docs`.

| Short name | Writes |
|---|---|
| `hardened-web` | An HTTP API on Kestrel, ASP.NET Core or AWS Lambda |
| `hardened-function` | An AWS Lambda function that is not an HTTP API |
| `hardened-library` | A module other applications compose |

## hardened-web

```bash
dotnet new hardened-web -n Todos [options]
```

| Option | Values | Default |
|---|---|---|
| `-ho, --host` | `kestrel`, `aspnet`, `aws-lambda` | `kestrel` |
| `-c, --contract` | `code`, `openapi`, `smithy` | `code` |
| `-rm, --response-model` | `response`, `throws`, `union` | `response` |
| `-cl, --client` | `kiota`, `refit`, `none` | `kiota` |
| `--openapi-ui` | `true`, `false` | `true` |
| `--hardened-version` | a published version | the version the template shipped with |
| `--skip-restore` | `true`, `false` | `false` |

### What you get

```
Todos.sln
.config/dotnet-tools.json        the client generator, Kiota or Refitter
Directory.Packages.props         every version, in one place
src/Todos/                       the implementation. Knows nothing about where it runs
src/Todos/openapi/Todos.json     the served document, written by the build and committed
src/Todos.Host/                  the runtime, and Program.cs
src/Todos.Client/                the generated client. No hand-written code
tests/Todos.Tests/               tests against the library, not the host
```

Swapping `--host` changes only the host project. The library, the client and the tests are the
same whichever host you pick.

### The host

`kestrel` serves HTTP through Kestrel without the ASP.NET Core request pipeline.

`aspnet` is for an application that needs ASP.NET Core's own middleware, authentication and
authorization, or its hosting diagnostics. Instrumentation that subscribes to the ASP.NET
`DiagnosticSource` names sees nothing under Kestrel. The [Kestrel host's README][kestrel] lists
the trade-offs.

`aws-lambda` puts the application behind API Gateway. The host project has no `Program.cs`,
because the generator writes the entry point Lambda invokes. A `Todos.Harness` project runs the
same application locally over HTTP, so `dotnet run --project src/Todos.Harness` still gives you
something to curl.

[kestrel]: https://github.com/ipjohnson/Hardened.Framework/blob/main/src/Web/Hardened.Web.Kestrel.Runtime/README.md

### The contract

`code`: the C# is the contract. Routes are attributes on methods, and the OpenAPI document is
generated from them.

`openapi`: an [OpenAPI document](/guide/openapi) is the contract. The models, the service
interface, the routes and the validation are generated from it.

`smithy`: the same, from a [Smithy model](/guide/smithy). The build runs the [Smithy CLI][smithy]
and names the version it expects if yours differs.

With `openapi` or `smithy` there are no route attributes in the project. Add an operation to the
contract and the build fails until the service implements it.

[smithy]: https://smithy.io/2.0/guides/smithy-cli/index.html

### The response model

`--response-model` decides how a handler declares more than one kind of response. The scaffolded
routes show the difference: they answer 404 and 409 in every mode, and creating a todo answers 201
under `response` and `union` and 200 under `throws`. [Declared responses](/guide/responses) covers
the three.

`union` writes a `net11.0` project pinned to the .NET 11 SDK in `global.json`. It cannot be
combined with `--host aws-lambda`, whose managed runtime is `net8.0`; the template refuses with
`HTPL001`.

`standard` is accepted as the old name for `throws` and writes the same project.

### The client

`kiota` writes a Kiota client under `src/Todos.Client`, generated during the build from the
document the library writes, and tests that drive it through the pipeline. `refit` writes a Refit
interface with Refitter instead, and every operation on it returns `IApiResponse<T>`. `none`
leaves out the client project and the tool manifest, and the same tests drive the pipeline through
`ITestWebApp`. See [Generated clients](/guide/clients) and [Typed clients](/guide/testing-clients).

### The reference page

`--openapi-ui` serves a page at `/docs` describing every operation, and the document behind it at
`/openapi.json`. It is served in the `development` environment only. Name more:

```csharp
[HardenedOpenApiUi(Title = "Todos", Environments = "development,staging")]
```

The environment is `HARDENED_ENVIRONMENT`, which defaults to `development`. See
[Environments](/guide/environments).

## hardened-function

```bash
dotnet new hardened-function -n OrderIntake [options]
```

| Option | Values | Default |
|---|---|---|
| `--trigger` | `invoke`, `sqs` | `invoke` |
| `--hardened-version` | a published version | the version the template shipped with |
| `--skip-restore` | `true`, `false` | `false` |

```
src/OrderIntake/            the function: its handler, models and services
tests/OrderIntake.Tests/    tests that invoke it the way Lambda does
```

The handler is a plain class:

```csharp
public class OrderHandler(OrderLog log) {

    [HardenedFunction]
    public Task<OrderAccepted> Process(Order order) { ... }
}
```

There is no host project and no `Program.cs`. The generator writes the entry point, so a `Main`
of your own gives the assembly a second entry point and the build fails.

With `--trigger sqs` the runtime unpacks the batch and calls the handler once per record.
Returning marks the record handled. Throwing reports it as a batch item failure, so only the
records that failed are redelivered.

There is nothing to `dotnet run`. The tests invoke the function through the real pipeline, with no
AWS account and nothing to deploy. See [Lambda functions](/aws/lambda-function).

## hardened-library

```bash
dotnet new hardened-library -n Acme.Greeting
```

A module that names no runtime, so one package serves Kestrel, ASP.NET Core and Lambda. The build
writes an attribute named after the module, and an application composes it the way it composes a
runtime:

```csharp
[HardenedModule]
[KestrelRuntime]
[AcmeGreetingLibrary]
public partial class Application;
```

There is no `AddAcmeGreeting()` to call and no options object to thread through. To carry HTTP
routes as well as services, add `[HardenedWebModule]` to the module class and reference
`Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator`. See [Modules](/guide/modules).

## Versions

Every template writes a `Directory.Packages.props` with one version for every Hardened package:

```xml
<HardenedVersion>0.20.0-rc1000</HardenedVersion>
```

It is the version the template package shipped with, and `--hardened-version` overrides it.
Generated code and the runtime it targets ship together, so the packages move as a set.

Templates do not update themselves. A newer release is a newer template package:

```bash
dotnet new install Hardened.Templates                     # latest
dotnet new install Hardened.Templates::0.20.0-rc1000      # a specific one
```

Existing projects keep the version in their own `Directory.Packages.props` until you change it.

The Lambda templates float `Hardened.Amz` at `0.*-*` rather than pinning it to `HardenedVersion`.
The two repositories release in sequence, so for a short window an exact pin would name a version
that does not exist yet.

## Each project explains itself

Every generated project carries a `README.md` on how it runs and how its projects fit together,
and an `AGENTS.md` with the invariants for whoever edits the code. Both are written for the
combination you chose.

## Next

- [Getting started](/guide/getting-started): the same project assembled by hand
- [Modules](/guide/modules): how `[HardenedModule]` composes
- [Writing a test](/guide/testing): what the scaffolded tests do
- [AWS](/aws/): the Lambda runtimes in depth
