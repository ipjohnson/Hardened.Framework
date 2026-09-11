# Project templates

`dotnet new hardened-web` writes a solution that builds, tests and serves. Two more templates
write a serverless function and a reusable library.

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
| `hardened-web` | An HTTP API on Kestrel, ASP.NET Core, AWS Lambda, Google Cloud Run or Azure Functions |
| `hardened-function` | A function that is not an HTTP API, on AWS Lambda, Google Cloud Run or Azure Functions |
| `hardened-library` | A module other applications compose |

## hardened-web

```bash
dotnet new hardened-web -n Todos [options]
```

| Option | Values | Default |
|---|---|---|
| `-ho, --host` | `kestrel`, `aspnet`, `aws-lambda`, `cloud-run`, `azure-functions` | `kestrel` |
| `-c, --contract` | `code`, `openapi`, `smithy` | `code` |
| `-rm, --response-model` | `response`, `throws`, `union` | `response` |
| `-cl, --client` | `kiota`, `refit`, `none` | `kiota` |
| `-s, --serializer` | `json`, `message-pack-named`, `message-pack-keyed` | `json` |
| `--test-framework` | `xunit`, `nunit` | `xunit` |
| `--mocks` | `nsubstitute`, `moq`, `fakeiteasy` | `nsubstitute` |
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

`aws-lambda` puts the application behind API Gateway. The host project has a `Program.cs` like
every other host, and the call to `LambdaEmulator.StartIfLocal` in it is what starts the AWS Lambda
Test Tool beside the process when it is not the Lambda service running it. The application answers
on 5080 through the tool's API Gateway emulator, so `dotnet run --project src/Todos.Host` and F5
work the way they do on the other hosts; see
[Running it locally](/aws/lambda-web#running-it-locally).

`cloud-run` runs the application as a Google Cloud Run service: the Kestrel host in a container,
listening on `PORT`, with `CloudRunHost.RunAsync` draining a request in flight when Cloud Run sends
`SIGTERM`. The template writes the `Dockerfile`, and `gcloud run deploy --source .` is the
deployment; see [Web services](/gcp/web).

`azure-functions` runs the application as an Azure Functions isolated worker: the host project's
`Program.cs` builds the worker with `ConfigureFunctionsWorkerDefaults` and `UseHardened<Application>()`,
its application writes `[HttpModule]` because the routes live in the library, and the generated
`Http` function catches every route. The template writes `host.json` with an empty route prefix
and `local.settings.json`; `func start` runs the same host locally, and `az functionapp create`
followed by `func azure functionapp publish` is the deployment. `--response-model union` is
refused with `HTPL007`, because the worker's managed runtime is `net8.0`; see
[Web applications](/azure/web).

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

`union` writes a `net11.0` project pinned to the .NET 11 SDK in `global.json`. It cannot be combined
with `--host aws-lambda`, whose managed runtime is `net8.0`, or with `--host azure-functions`, whose
worker runs the versions the Functions host supports.

Either combination is refused at instantiation: `dotnet new` prints the reason and exits non-zero.
The files are written first and stay, because the template engine does not unwind what it created,
so the first build of them refuses again with `HTPL001`.

`standard` is accepted as the old name for `throws` and writes the same project.

### The client

`kiota` writes a Kiota client under `src/Todos.Client`, generated during the build from the
document the library writes, and tests that drive it through the pipeline. `refit` writes a Refit
interface with Refitter instead, and every operation on it returns `IApiResponse<T>`. `none`
leaves out the client project and the tool manifest, and the same tests drive the pipeline through
`ITestWebApp`. See [Generated clients](/guide/clients) and [Typed clients](/guide/testing-clients).

### The serializer

`--serializer` decides what an operation can answer besides JSON. Both MessagePack modes wire the
package, the module attribute, the media types on every scaffolded route and — with
`--client refit` — the two Liquid templates that put the same attributes on the generated client.

`message-pack-named` identifies each member on the wire by the name the document publishes.
`message-pack-keyed` identifies it by an integer the contract states: smaller on the wire, and the
identity survives a rename. Nothing assigns an index — an index the build chose would move the next
time a property was added above it — so a member without one is a build error naming the member and
the next free index.

It cannot be combined with `--contract smithy`. A Smithy model states its wire format through its
protocol trait, and there is no MessagePack protocol to state, so the contract has nowhere to name
the media type and nowhere to state an index. Refused at instantiation the way the union
combinations are, with `HTPL008` as the build's backstop.

[MessagePack](/guide/message-pack) covers the rest, including what it does not cover.

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
| `--trigger` | `invoke`, `queue`, `topic`, `timer`, `change`, `stream`, `blob` | `invoke` |
| `-ho, --host` | `aws`, `gcp`, `azure` | `aws` |
| `--test-framework` | `xunit`, `nunit` | `xunit` |
| `--mocks` | `nsubstitute`, `moq`, `fakeiteasy` | `nsubstitute` |
| `--hardened-version` | a published version | the version the template shipped with |
| `--skip-restore` | `true`, `false` | `false` |

```
src/OrderIntake/               the function: its handler, models and services
src/OrderIntake/Program.cs     the entry point, and the local emulator
tests/OrderIntake.Tests/       tests that invoke it the way Lambda does
```

The handler is a plain class:

```csharp
public class OrderHandler(OrderLog log) {

    [HardenedFunction]
    public Task<OrderAccepted> Process(Order order) { ... }
}
```

There is no separate host project. The deployed artifact is this assembly, and `Program.cs` is the
entry point the runtime starts — written rather than generated, so do not add a `Main` of your own.

`--trigger` picks which source the scaffolded handler serves, and with it the one adapter package
the project references. Each is a [trigger attribute](/guide/triggers) naming the queue, topic,
schedule, table, stream or bucket, and nothing in the project names a cloud:

| `--trigger` | Handler carries | AWS adapter | Cloud Run adapter | Azure adapter |
|---|---|---|---|---|
| `invoke` | `[HardenedFunction]` | `Hardened.Aws.Lambda.Invoke` | `Hardened.Gcp.CloudRun.Invoke` | none; `HTPL006` refuses the combination |
| `queue` | `[Queue("orders")]` | `Hardened.Aws.Lambda.Sqs` | `Hardened.Gcp.CloudRun.PubSub` | `Hardened.Azure.Functions.ServiceBus` |
| `topic` | `[Topic("orders")]` | `Hardened.Aws.Lambda.Sns` | `Hardened.Gcp.CloudRun.PubSub` | `Hardened.Azure.Functions.ServiceBus` |
| `timer` | `[Timer("nightly")]` | `Hardened.Aws.Lambda.EventBridge` | `Hardened.Gcp.CloudRun.Scheduler` | `Hardened.Azure.Functions.Timer` |
| `change` | `[Change("orders")]` | `Hardened.Aws.Lambda.DynamoDb` | `Hardened.Gcp.CloudRun.Firestore` | `Hardened.Azure.Functions.CosmosDb` |
| `stream` | `[Stream("orders")]` | `Hardened.Aws.Lambda.Kinesis` | none; `HTPL005` refuses the combination | `Hardened.Azure.Functions.EventHubs` |
| `blob` | `[Blob("uploads")]` | `Hardened.Aws.Lambda.S3` | `Hardened.Gcp.CloudRun.Storage` | `Hardened.Azure.Functions.Blobs` |

On a batched trigger the runtime unpacks the batch and calls the handler once per item. Returning
handles the item; throwing fails the invocation, which is what returns the batch to the source.
Reporting individual failures instead is a deployment setting the application has to opt into, and
it has to match the event source mapping — see
[Batches](/guide/triggers#batches-and-what-a-failure-means).

Running the project starts the AWS Lambda Test Tool on 5050, which is where a payload is posted;
there is no HTTP API and nothing on 5080. Most of the time there is nothing to run, because the
tests invoke the function through the real pipeline with no AWS account and nothing to deploy. See
[Lambda functions](/aws/lambda-function).

With `--host gcp` the application names its host, `[CloudRunRuntime]`, because a Cloud Run service
is a container listening on a port. Running the project is the service on 8080, or `PORT`, and a
trigger is an HTTP request that can be posted to it by hand; there is no emulator to start. The
template writes the `Dockerfile`, and the README shows the `gcloud` command that wires each source
to the deployed service. See [Google Cloud Run](/gcp/).

With `--host azure` the application names nothing: the handler's trigger picks the adapter, and
the generator writes the function the Functions host indexes. `Program.cs` builds the isolated
worker with `ConfigureFunctionsWorkerDefaults` and `UseHardened<Application>()`, and the project
references `Microsoft.Azure.Functions.Worker.Sdk`, which writes the host's metadata beside the
build output. `func start --script-root src/OrderIntake/bin/Debug/net8.0` runs the same host
locally against `local.settings.json`, with Azurite for the host's storage and the Service Bus or
Event Hubs emulator for the source. A topic writes `[ServiceBusModule(Subscription = "...")]` and
a change feed `[CosmosDbModule(Database = "...")]` on the application, because those are
deployment facts the trigger has no slot for. There is no IaC; the README shows the `az` commands
that create the function app and the setting that wires each source. See
[Azure Functions](/azure/).

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
<HardenedVersion>0.33.0-rc1000</HardenedVersion>
```

It is the version the template package shipped with, and `--hardened-version` overrides it.
Generated code and the runtime it targets ship together, so the packages move as a set.

Templates do not update themselves. A newer release is a newer template package:

```bash
dotnet new install Hardened.Templates                     # latest
dotnet new install Hardened.Templates@0.33.0-rc1000       # a specific one
```

Existing projects keep the version in their own `Directory.Packages.props` until you change it.

The Lambda templates used to float a `Hardened.Amz` pin, because the AWS packages released from a
second repository and for a window an exact pin named a version that did not exist yet. There is
one repository and one line now, so every template pins `HardenedVersion` like everything else.

## Each project explains itself

Every generated project carries a `README.md` on how it runs and how its projects fit together,
and an `AGENTS.md` with the invariants for whoever edits the code. Both are written for the
combination you chose.

## Next

- [Getting started](/guide/getting-started): the same project assembled by hand
- [Modules](/guide/modules): how `[HardenedModule]` composes
- [Writing a test](/guide/testing): what the scaffolded tests do
- [AWS](/aws/): the Lambda runtimes in depth
- [Google Cloud Run](/gcp/): the Cloud Run runtime in depth
- [Azure Functions](/azure/): the Functions runtime in depth
