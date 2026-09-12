# Azure Functions application types

Every Azure Functions application is one entry point class carrying `[HardenedModule]` and
nothing that names Azure. Which sources it serves is said by the trigger attributes on its
handlers, and each trigger is served by one adapter package that binds a build property the
generator reads. What the host runs is written by the generator from those handlers.

| Source | Handler carries | Package | Function | Route |
|---|---|---|---|---|
| HTTP | `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]` | `Hardened.Azure.Functions.Http` | `Http`, one for every route | The route the attribute names |
| A Service Bus queue | `[Queue]` | `Hardened.Azure.Functions.ServiceBus` | `Queue_{queue}` | `QUEUE /{queue}` |
| A Service Bus subscription | `[Topic]` | `Hardened.Azure.Functions.ServiceBus` | `Topic_{topic}` | `TOPIC /{topic}` |
| A timer | `[Timer]` | `Hardened.Azure.Functions.Timer` | `Timer_{name}` | `TIMER /{name}` |
| An Event Hubs hub | `[Stream]` | `Hardened.Azure.Functions.EventHubs` | `Stream_{hub}` | `STREAM /{hub}` |
| The Cosmos DB change feed | `[Change]` | `Hardened.Azure.Functions.CosmosDb` | `Change_{container}` | `CHANGE /{container}` |
| Blob Storage | `[Blob]` | `Hardened.Azure.Functions.Blobs` | `Blob_{container}` | `BLOB /{container}` |
| Event Grid | `[Event]` | `Hardened.Azure.Functions.EventGrid` | `Event`, one for every handler | `EVENT /{source}/{type}` |

`[HardenedFunction]` has no row. Azure Functions has no direct invocation of a function, and a
caller reaches a function app over HTTP, which is a web route; a project that writes it fails with
`HRDF001` naming the gap. That is decision D4 of the cloud lines plan for this line.

Unlike Lambda, where the invocation loop is the host and the adapter recognises the payload, and
unlike Cloud Run, where every source is an HTTP request the front door tells apart, the Functions
host does the recognising: it binds one function per source and invokes the worker with data the
extension has already typed. The adapter's job is to turn that data into the request a handler
meets.

## Five rules

1. **`[HardenedModule]` is always required.** It is what marks a class as an entry point.
2. **Nothing on the application names the host.** A function app is whatever the Functions host
   starts, and the host is told what to start by the build. `[HttpModule]` is the one adapter a
   host project writes out, when its routes live in a library, for the reason a Lambda host writes
   `[LambdaHttpModule]`: a generator sees only the compilation it runs in.
3. **A trigger attribute is what pulls an adapter in.** `[Queue]` on a handler makes the generator
   read `HardenedQueueModule` from the referenced package's build properties, register
   `ServiceBusModule`, and write `Queue_orders`. An adapter is written out on the application
   only to carry a deployment fact, and every such fact is a nullable property on the module.
4. **Reference the source generators as analyzers, and the Worker SDK as a package.**
   `Hardened.Library.SourceGenerator` always, `Hardened.Function.SourceGenerator` for a project
   with trigger handlers, `Hardened.Web.SourceGenerator` for one with routes, and
   `Hardened.Azure.Functions.SourceGenerator` for the host's view. `Microsoft.Azure.Functions.Worker.Sdk`
   is the application's reference and not the runtime package's; `HRDAZ010` names it when it is
   missing.
5. **`Program.cs` is written, not generated.** It is the worker's entry point, and it is the same
   file for every application type.

## The entry point

```csharp
using Hardened.Azure.Functions.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using OrderIntake;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<Application>())
    .Build();

host.Run();
```

`ConfigureFunctionsWorkerDefaults` is the worker's own: the gRPC channel to the host, the
converters, the default services. `UseHardened<T>()` adds logging if nothing did, the
environment, the application's container, and the two generated types the host needs:

- an `IFunctionMetadataProvider`, which is what the worker answers when the host asks for the
  functions it serves. The Worker SDK's own generated provider is kept out by the runtime
  package's targets, because it sees none of Hardened's shims and would answer with nothing.
- an `IFunctionExecutor`, which is what runs when the host invokes a function: it binds the
  shim's parameters through the worker's converters and calls the shim.

`ConfigureFunctionsWebApplication` is never used. It starts an ASP.NET Core server in the worker
and hands the HTTP trigger `HttpRequest`; this line binds `HttpRequestData`, which arrives over
the worker channel, and no adapter references `Hardened.Web.Runtime` or ASP.NET Core.

The project is an `Exe`. The Worker SDK scans it for `[Function]` methods, writes
`functions.metadata` and `worker.config.json` beside the output and builds the host's extensions
into `.azurefunctions/`, which is what the container tier mounts into the host image and what
`func azure functionapp publish` uploads.

## What the generator writes

For an application, `AzureFunctionsSourceGenerator` writes two files.

`{App}.AzureFunctions.cs` holds a static shim class, the metadata provider and the executor. A
shim is a `[Function]` method wearing the extension's trigger attribute, which is what the Worker
SDK scans and what the host binds:

```csharp
[Function("Queue_orders")]
public static Task Queue_orders(
    [ServiceBusTrigger("orders", IsBatched = true)] ServiceBusReceivedMessage[] messages,
    ServiceBusMessageActions messageActions,
    FunctionContext context) =>
    FunctionsInvocationHandler.Invoke(
        context, "QUEUE", "/orders", new ServiceBusDelivery(messages, messageActions),
        FunctionsDispatch.Trigger);
```

The provider returns one `DefaultFunctionMetadata` per shim with the raw binding JSON the SDK's
build task would have written for the same attribute, name for name: the trigger type, the
constructor arguments under their parameter names, the named arguments camel-cased, the
cardinality, and `supportsDeferredBinding` where the extension's converter advertises the type.
`MetadataAgreement` in the testing package holds the two to the same answer, and every fixture
asserts it.

The executor switches on the function's name, binds the inputs through the worker's
`IFunctionInputBindingFeature` and calls the shim, so a shim is invoked exactly as the SDK's own
executor would invoke it.

`{App}.AzureFunctionsWorker.cs` is the partial that `UseHardened` calls to register both.

The binding table is `AzureBinding`, one row per family: the attribute, the shim's parameters,
the settings the row reads off the module, and the JSON it writes. A family whose module is bound
and whose trigger is declared gets a function; one whose module is bound and whose trigger is not
declared is left to `HRDF003`; one whose trigger is declared and whose module is not bound is
`HRDF001`, once, from the library generator.

## Deployment facts on the module

Three of the families need something the neutral trigger has no slot for, and one has a switch
the code cannot know:

| Module | Property | Written into | Required |
|---|---|---|---|
| `ServiceBusModule` | `Subscription` | `subscriptionName` on every topic function | Yes, for `[Topic]`: `HRDAZ003` |
| `ServiceBusModule` | `Connection` | `connection` | No; the extension's default is `AzureWebJobsServiceBus` |
| `ServiceBusModule` | `ReportsItemFailures` | `autoCompleteMessages: false`, and the adapter settles each message | No; off by default |
| `EventHubsModule` | `Connection`, `ConsumerGroup` | `connection`, `consumerGroup` | No; the connection defaults to `AzureWebJobsEventHubs`, because the extension has no default and a trigger without one fails the host at startup |
| `EventHubsModule`, `CosmosDbModule` | `RetryCount`, `RetryDelay` | `[FixedDelayRetry]` on the function and `Retry` on the provider's metadata, which the worker sends the host as the `retry` block | No, but both or neither: `HRDAZ003` names the missing half |
| `CosmosDbModule` | `Database` | `databaseName` on every change function | Yes: `HRDAZ003` |
| `CosmosDbModule` | `Connection`, `LeaseContainer` | `connection`, `leaseContainerName` | No |
| `BlobsModule` | `Connection` | `connection` | No; the extension's default is `AzureWebJobsStorage` |

The generator reads them off the attribute the application wrote, `[ServiceBusModule(Subscription
= "orders-service")]`, as text for the shim's attribute and as a literal for the provider's JSON.
A value that is not a literal is `HRDAZ004`: the shim could carry it and the metadata the host
reads could not. The timer's schedule is not a module property at all; it is the app setting
`%Hardened:Timers:{name}%`, so a deployment decides when `[Timer("nightly")]` runs.

## The invocation handler

`FunctionsInvocationHandler.Invoke` is what every shim calls. It selects the adapter by the data's
type and, for the three families that bind a string, the scheme; builds the request through the
adapter; runs the pipeline with the adapter's failure policy; rethrows a failure the IO filter
recorded when the policy is `Rethrow`, because an unbatched request has no batch filter to do it
and returning normally would tell the host the invocation succeeded; and answers the host with
what the adapter writes, which is null for every family but HTTP.

Dispatch is selected per invocation, from what the shim said: a trigger goes to
`FunctionDispatchFilter` and the HTTP function to the web dispatch, through a `DispatchSelector`
installed once. That is what lets a `[Get]` and a `[Queue]` live in one function app, and it is
deliberately not the Lambda handler's refusal of two dispatches, which the GCP line found wrong for
a host that serves both.

## Settlement

A Service Bus function's shim always binds `ServiceBusMessageActions`, the settlement channel the
worker gets from the host. With `ReportsItemFailures` off, the adapter ignores it and the host
completes the batch when the invocation succeeds and abandons it when it fails, which is the
default every queue adapter in the framework has. With it on, the generated binding says
`autoCompleteMessages: false` and the adapter, after the batch filter has attempted every message,
completes each that succeeded and abandons each that failed, by message id, and the invocation
succeeds. `RecordingMessageActions` in the testing package is the channel a test hands in, and
the settlement fixture asserts what it recorded.

## What a failure does not do

Every family rethrows, so a failed handler is a failed invocation, which the host logs and counts.
What the host does next is the source's own rule, and two of them differ from their AWS
counterparts in a way the adapters document rather than hide:

- **Event Hubs and the change feed do not replay a failed batch.** The host advances the
  partition's checkpoint and the extension the lease when the invocation completes, with or
  without an exception (functions-reliable-event-processing; the error-handling page lists a
  retry policy as both triggers' only retry). Kinesis and DynamoDB Streams on Lambda retry until
  the batch succeeds or expires. A handler that has to see every event on Azure declares a retry
  policy on its module, `RetryCount` and `RetryDelay`, which the generator writes as
  `[FixedDelayRetry]` on the function and as `DefaultRetryOptions` on the provider's metadata;
  the worker maps the second into the `retry` block the host reads. It needs a dead-letter path
  of its own when the policy is spent.
- **The change feed carries the current document only.** There is no old image and no raw form,
  so `[OldImage]` on DynamoDB and `[OldValue]` on Firestore have no counterpart; the document is
  bound with the `_lsn`, `_ts` and `_etag` Cosmos stamped on it, and those are headers too.
- **A blob handler binds the notification, not the blob.** The container, the name, the size and
  the event, projected the way the S3 adapter projects them. The shim binds a `BlobClient`, which
  costs no request, and a handler that wants the content fetches it.

## Native AOT and trimming

Every runtime package in the line passes the trim and AOT analyzers, and the repository publishes
`Hardened.IntegrationTests.Aot.AzureFunctions.SUT` twice. With `PublishAot`, ILC compiles the
whole graph, and the publish fails on a trim or AOT warning from any assembly but the worker's
own; that is the gate. Trimmed and self-contained, the same project is what the real host runs in
CI: it indexes both functions from the generated provider and serves the web route through the
trimmed worker.

The native binary is not what the host runs, because the worker package cannot start as one.
`Microsoft.Azure.Functions.Worker.Core`'s `WorkerInformation` reads
`FileVersionInfo.GetVersionInfo(typeof(WorkerInformation).Assembly.Location)` in a static
initializer, `Assembly.Location` is empty under Native AOT, and the worker dies in
`WorkerHostedService.StartAsync` before it opens the channel to the host. That is inside the
worker, reachable by no package setting, and it is where the maintainers' statement that Native
AOT is not supported (Azure/azure-functions-dotnet-worker#2715, tracked under #1056) becomes a
fact rather than a caveat. A trimmed worker has its assemblies on disk and does not hit it.

Three further things stood between a trimmed worker and a running one, none of them in this
line's code, and the runtime package's targets record each:

- `worker.config.json` names the executable. The Worker SDK writes the apphost's name when the
  publish is self-contained and the apphost is among the None items; a Native AOT publish on the
  .NET SDK this repository builds with creates no apphost, so the file named `dotnet` and a `.dll`
  the publish did not contain, and the host refuses a worker path that names a missing file. The
  targets rewrite the file for a native publish, so the output is well-formed for the day the
  worker starts natively.
- Reflection-based `System.Text.Json` stays on. The worker parses every raw binding a provider
  returns with `JsonSerializer.Deserialize<JsonElement>(string)` and no resolver, which both a
  trimmed and a native publish turn off by default; the worker's own generated provider goes
  through the same call (#2715).
- The worker's assemblies, each extension's and the application's are rooted whole. The worker
  activates the extension startup class the SDK generates into the application by name, and
  every input converter through `ActivatorUtilities` from a `Type`; the trimmer keeps the types
  and removes their constructors, and the worker died on the first.

The trim and AOT warnings that remain are the worker's own - `Microsoft.Azure.Functions.Worker.Core`,
`.Grpc`, the extensions and `Google.Protobuf` under the gRPC channel - and both publishes fail
on a warning from any other assembly. Hardened's are clean, and no adapter is kept out of the
proof.

## Project shape

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" />
    <PackageReference Include="Hardened.Shared.Runtime" />
    <PackageReference Include="Hardened.Requests.Runtime" />
    <PackageReference Include="Hardened.Functions.Runtime" />
    <PackageReference Include="Hardened.Azure.Functions.Runtime" />
    <PackageReference Include="Hardened.Azure.Functions.ServiceBus" />
    <PackageReference Include="Hardened.Library.SourceGenerator" />
    <PackageReference Include="Hardened.Function.SourceGenerator" PrivateAssets="all" />
    <PackageReference Include="Hardened.Azure.Functions.SourceGenerator" PrivateAssets="all" />
</ItemGroup>
```

Inside this repository the packages are project references, and a project reference carries no
build assets, so a fixture imports the `.targets` files from the source tree:
`Hardened.Functions.Runtime.targets` declares the trigger properties, each adapter's binds one,
and `Hardened.Azure.Functions.Runtime.targets` keeps the SDK's generated pair out and names the
native worker. A consumer gets the same files from `buildTransitive/`.

## Running locally

`func start` from Azure Functions Core Tools is the host, started against the project's build
output, reading `local.settings.json` for what a deployment puts in the environment. The host
needs a storage account of its own, which is Azurite under `UseDevelopmentStorage=true`, and each
source needs its namespace or emulator: the Service Bus and Event Hubs emulators run in Docker and
answer `UseDevelopmentEmulator=true` connection strings. The host indexes the functions before it
starts a listener, so a function app comes up and reports the listener until its source is
reachable.

## Testing

Three rungs, all on the same test methods. The pipeline rung is `[assembly: FunctionTesting]` and
names no cloud. The worker rung is `[assembly: AzureFunctionsTesting]`: `FunctionsTriggerDelivery`
builds the trigger data the worker would bind, through the SDKs' model factories, and hands it to
the real invocation handler; `[assembly: AzureFunctionsWebTesting]` does the same for a web
application through `TestHttpRequestData`, a subclass of the worker's own abstract request. The
container rung is a project per family in `Hardened.Simulators.slnx`, mounting the fixture's build
output into the host image beside Azurite and the Service Bus or Event Hubs emulator, observed
through `HARDENED-OBSERVED` lines the fixture prints. The repository's fixtures compile one test
file into two projects, one per in-process rung.

`[Event]` has no façade, because its route has two segments; its tests send through
`ITriggerDelivery` directly, and both rungs route it. A class can opt back to the pipeline
delivery with `[PipelineDelivery]` from `Hardened.Functions.Testing`.

## Building

```bash
dotnet build filters/azure.slnf
dotnet test  filters/azure.slnf
dotnet build filters/azure.slnf --configuration Release -p:ContinuousIntegrationBuild=true
```

The third is the gate CI applies; it turns warnings into errors, and the trim and AOT analyzers
are on in every runtime package through `IsAotCompatible`. The container tier is `dotnet test`
on a project in `Hardened.Simulators.slnx`, one at a time, because two emulator sets on one
machine contend for ports and memory.

## Failures

**`HRDF001` naming `[HardenedFunction]`**
There is no Azure adapter for it. Write the operation as a web route, which is
`hardened-web --host azure-functions`.

**`HRDAZ003` naming `Subscription` or `Database`**
A topic is read through a subscription and a container lives in a database, and the trigger has
no slot for either. Write `[ServiceBusModule(Subscription = "...")]` or
`[CosmosDbModule(Database = "...")]` on the application.

**`HRDAZ004` naming a setting**
The value is not a literal. The host reads the metadata, not the compiled attribute, so a
constant from another class cannot reach it; write the string.

**The host logs "No event hub receiver named ..." and fails to start**
A stream function whose binding names no connection, which the generator now never writes; a
hand-written `[EventHubTrigger]` beside Hardened's needs a `Connection`.

**`HRDAZ010` naming `Microsoft.Azure.Functions.Worker.Sdk`**
The executable references the runtime package and not the Sdk, so nothing writes
`worker.config.json` and the host would start no worker.

**A function app publishes and indexes no functions**
The Worker SDK's generated provider was registered ahead of Hardened's, which the runtime
package's targets prevent when they are imported; a project that imports the adapter's targets
and not the runtime's has this shape.

**The Functions host indexes the functions and the web route answers 404**
The route prefix. The host serves under `api` unless `host.json` says otherwise, and the adapter
takes the prefix off through the catch-all route's value; a request to the raw path without the
prefix never reaches the function. The web template's `host.json` clears the prefix.
