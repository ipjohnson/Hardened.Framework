# Azure

A Hardened function app runs on Azure Functions as a .NET isolated worker. It references
`Hardened.Azure.Functions.Runtime`, one adapter package for each trigger its handlers use,
`Hardened.Azure.Functions.SourceGenerator` and `Microsoft.Azure.Functions.Worker.Sdk`.

`dotnet new hardened-function -n Orders --host azure --trigger queue` writes a function app with
tests. This is its handler, `src/Orders/OrderHandler.cs`, without the template's comments:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

This is its application class, `src/Orders/Application.cs`, also without comments:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The build writes the Azure functions that the Functions host indexes and invokes. With
`Hardened.Azure.Functions.ServiceBus` referenced, `[Queue("orders")]` becomes the function
`Queue_orders`, bound to the Service Bus queue named `orders`. The Functions host lists the function
when `func start` runs:

```console
$ func start
Functions:

	Queue_orders: serviceBusTrigger
```

The handler and the application class name no Azure service. [Triggers](/guide/triggers) covers
the trigger attributes and what they bind on every cloud.

With `--host azure`, the template needs `--trigger`. The default trigger, `invoke`, fails the build
with `HTPL006`. [Project templates](/guide/project-templates) covers the options.

A web application on Azure Functions is the `hardened-web` template with `--host azure-functions`.
[Web applications](/azure/web) covers it.

## Packages

| Package | Serves | Module attribute | Build property |
|---|---|---|---|
| `Hardened.Azure.Functions.Runtime` | The worker registration `UseHardened<TApplication>` and `FunctionsInvocationHandler`, which runs each invocation. Every Azure project references it | None. Each adapter's module brings `FunctionsRuntimeModule` | None |
| `Hardened.Azure.Functions.ServiceBus` | `[Queue]`, from a Service Bus queue, and `[Topic]`, from a subscription of a Service Bus topic | `[ServiceBusModule]` | `HardenedQueueModule` and `HardenedTopicModule` |
| `Hardened.Azure.Functions.Timer` | `[Timer]`, from a timer trigger | `[TimerModule]` | `HardenedTimerModule` |
| `Hardened.Azure.Functions.EventGrid` | `[Event]`, from Event Grid in the CloudEvents schema | `[EventGridModule]` | `HardenedEventModule` |
| `Hardened.Azure.Functions.CosmosDb` | `[Change]`, from the Cosmos DB change feed | `[CosmosDbModule]` | `HardenedChangeModule` |
| `Hardened.Azure.Functions.EventHubs` | `[Stream]`, from Event Hubs | `[EventHubsModule]` | `HardenedStreamModule` |
| `Hardened.Azure.Functions.Blobs` | `[Blob]`, from Blob Storage, through Event Grid | `[BlobsModule]` | `HardenedBlobModule` |
| `Hardened.Azure.Functions.Http` | `[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]`, behind one HTTP function | `[HttpModule]` | `HardenedHttpModule` |
| `Hardened.Azure.Functions` | Every adapter above, in one reference | Every attribute above | Every property above |
| `Hardened.Azure.Functions.SourceGenerator` | The generator that writes the functions, the metadata provider that lists them and the executor that runs them. Every Azure project references it | None | None |
| `Hardened.Azure.Functions.Testing` | `[AzureFunctionsTesting]` and `[AzureFunctionsWebTesting]`, in a test project | None | None |

Azure has no adapter for `[HardenedFunction]`. Every other trigger has an adapter. A project with a
`[HardenedFunction]` handler fails to build with `HRDF001`:

```text
error HRDF001: Handlers in this project use [HardenedFunction], but no referenced runtime declares a module for it. Reference a runtime package that supports HardenedFunction triggers, or set <HardenedInvokeModule> to the module that should serve them.
```

The project references `Microsoft.Azure.Functions.Worker.Sdk` itself. An executable that references
`Hardened.Azure.Functions.Runtime` and not the SDK fails to build with `HRDAZ010`. The template
keeps package versions in `Directory.Packages.props`, and its project file lists the references
without them. Without central package management, the queue function's package references are
these:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Requests.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
  <PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Azure.Functions.ServiceBus" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Function.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
  <PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
</ItemGroup>
```

The template pins `Microsoft.Azure.Functions.Worker.Sdk` at 2.1.0.

`Hardened.Azure.Functions` puts the assemblies of every adapter in the build output, whatever the
handlers use. In a queue function, the build notes the five other trigger adapters as `HRDF003`.
[Triggers](/guide/triggers) covers `HRDF003`. The package does not include the generator. The
project references `Hardened.Azure.Functions.SourceGenerator` beside it.

## Module attributes

An application does not write an adapter's module attribute to get the adapter. The build registers
the module from the build property, in the generated `Application.TriggerModules.cs`.
[Triggers](/guide/triggers) covers the mechanism. `FunctionsRuntimeModule` registers the invocation
handler and the request pipeline.

An application writes a module attribute to change a setting, such as
`[ServiceBusModule(Subscription = "audit")]`. The build then leaves out its own registration of
that module. It writes the setting into the bindings of the module's functions. Each module
attribute is in the namespace named like its package, such as `Hardened.Azure.Functions.ServiceBus`.

A setting that reaches a binding has to be a string literal, an integer literal, `true` or `false`.
Anything else, such as a constant declared in another class, fails the build with `HRDAZ004`.

Each trigger's page covers its module's settings:

| Module attribute | Settings | Page |
|---|---|---|
| `[ServiceBusModule]` | `Subscription`, `Connection`, `ReportsItemFailures` | [Queues](/azure/queue), [Topics](/azure/topic) |
| `[TimerModule]` | None | [Timers](/azure/timer) |
| `[EventGridModule]` | None | [Events](/azure/event) |
| `[CosmosDbModule]` | `Database`, `Connection`, `LeaseContainer`, `RetryCount`, `RetryDelay` | [Changes](/azure/change) |
| `[EventHubsModule]` | `Connection`, `ConsumerGroup`, `RetryCount`, `RetryDelay` | [Streams](/azure/stream) |
| `[BlobsModule]` | `Connection` | [Blobs](/azure/blob) |
| `[HttpModule]` | None | [Web applications](/azure/web) |

## Generated functions

The application class in the example above is a `partial` class marked `[HardenedModule]`, with no
Azure attribute. `Hardened.Azure.Functions.SourceGenerator` writes two files for it:

| File | Contents |
|---|---|
| `Application.AzureFunctions.cs` | The functions, a metadata provider that lists them and an executor that runs them |
| `Application.AzureFunctionsWorker.cs` | The code that registers the provider and the executor with the worker |

The template sets `EmitCompilerGeneratedFiles`, so the build writes both files under
`obj/Debug/net8.0/generated/Hardened.Azure.Functions.SourceGenerator/`. The Functions host indexes
the functions that the generated provider lists. The Worker SDK also writes `functions.metadata`
from the same functions. [Testing](/azure/testing) covers `MetadataAgreement`, which checks that
the two descriptions agree.

The table shows the function that the build writes for each handler:

| Handler | Function | Trigger binding on the function | What the worker binds |
|---|---|---|---|
| `[Queue("orders")]` | `Queue_orders` | `ServiceBusTrigger("orders", IsBatched = true)` | `ServiceBusReceivedMessage[]`, and `ServiceBusMessageActions` beside it |
| `[Topic("order-events")]` | `Topic_order_events` | `ServiceBusTrigger("order-events", "audit", IsBatched = true)`, with the module's `Subscription` | `ServiceBusReceivedMessage[]`, and `ServiceBusMessageActions` beside it |
| `[Timer("nightly")]` | `Timer_nightly` | `TimerTrigger("%Hardened:Timers:nightly%")` | The timer's state, as JSON text |
| `[Stream("clicks")]` | `Stream_clicks` | `EventHubTrigger("clicks", IsBatched = true, Connection = "AzureWebJobsEventHubs")` | `EventData[]` |
| `[Change("orders")]` | `Change_orders` | `CosmosDBTrigger("shop", "orders", CreateLeaseContainerIfNotExists = true)`, with the module's `Database` | The changed documents, as JSON text |
| `[Blob("uploads")]` | `Blob_uploads` | `BlobTrigger("uploads/{name}", Source = BlobTriggerSource.EventGrid)` | `BlobClient` |
| Every `[Event]` handler | `Event` | `EventGridTrigger` | The CloudEvent, as JSON text |
| Every web route | `Http` | `HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "patch", "delete", "head", "options", Route = "{*path}")` | `HttpRequestData` |

A queue, topic, timer, stream, change or blob handler gets a function of its own. The function's
name is the trigger's name, an underscore, and the source's name with each character that is not a
letter or a digit replaced by an underscore. Two handlers whose sources give the same name fail the
build with `HRDAZ002`. For example, `[Queue("orders-new")]` and `[Queue("orders_new")]` both give
`Queue_orders_new`.

Every `[Event]` handler shares the one function `Event`. Every web route shares `Http`. Each
function hands its invocation to the handler's route, such as `QUEUE /orders`. `Event` routes each
event by its source and type, as `EVENT /com.acme.orders/OrderPlaced`. `Http` routes each request by
its method and path. [Triggers](/guide/triggers) and the trigger pages, such as
[Events](/azure/event), cover routing.

A binding that needs a module setting the application did not write fails the build with
`HRDAZ003`. A `[Topic]` binding needs `Subscription`, which [Topics](/azure/topic) covers. A
`[Change]` binding needs `Database`, which [Changes](/azure/change) covers. The timer's schedule is
the app setting that its binding names, such as `Hardened:Timers:nightly`. [Timers](/azure/timer)
covers it.

## Handlers in one function app

| Handlers | What happens |
|---|---|
| Any mix of `[Queue]`, `[Topic]`, `[Timer]`, `[Event]`, `[Change]`, `[Stream]` and `[Blob]` | Each source reaches its handler through its own function |
| Trigger handlers and web routes | The routes answer through the `Http` function beside the trigger functions. The project also references `Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator`, and the application class carries `[HardenedWebModule]`. The function template's `host.json` keeps the host's `api` route prefix, so the routes answer under `/api`. [Web applications](/azure/web) covers the prefix |
| A `[HardenedFunction]` handler | The build fails with `HRDF001` |
| An event whose source and type no `[Event]` handler declares | The invocation fails with `No handler is registered for EVENT /com.acme.orders/OrderShipped. An event source is wired to this function that no trigger attribute declared.` |

## The entry point

A function app's entry point is `Program.cs`, which the template writes. Nothing generates it. This
is `src/Orders/Program.cs`, without the template's comments:

```csharp
using Hardened.Azure.Functions.Runtime.Hosting;
using Microsoft.Extensions.Hosting;
using Orders;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<Application>())
    .Build();

host.Run();
```

`ConfigureFunctionsWorkerDefaults` makes the process an isolated worker. The Functions host starts
the worker and sends it each invocation.

`UseHardened<TApplication>` is in `Hardened.Azure.Functions.Runtime.Hosting`. It adds the
application's services to the worker's service collection. It also registers the generated metadata
provider and executor with the worker. `TApplication` is the application class. The generator makes
it an `IHardenedFunctionsApplication`. [Triggers](/guide/triggers) covers the build error that a
project with no adapter package gets at this call.

`UseHardened` takes an optional `IHardenedEnvironment`. Without one, it registers an environment
built from the process's command line. [Environments](/guide/environments) covers the environment.
[Hosts](/guide/hosts) shows the web application's `Program.cs`, which passes the environment to
`UseHardened`.

The worker runs the application's startup services, its `IStartupService` registrations, when it
starts, before it connects to the Functions host. [Modules](/guide/modules) covers startup services.

The worker starts without creating any handler. When a handler needs a service that nothing
registers, the worker still starts. Each invocation that reaches that handler fails.

A handler's `CancellationToken` is the worker's `FunctionContext.CancellationToken`. The Functions
host cancels it when it stops the invocation. [Parameter binding](/guide/parameter-binding) covers
the parameter.

## Running locally

In `src/Orders`, `func start` from
[Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-core-tools-reference)
builds the project, starts the Functions host and lists the functions it indexed. The host listens
on port 7071. `--port` changes it. Core Tools prints a hint to use `dotnet run`. `dotnet run` does
not start these projects. [Hosts](/guide/hosts) covers it.

The host reads the settings in `src/Orders/local.settings.json`, which the template writes:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsServiceBus": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureWebJobsStorage": "UseDevelopmentStorage=true"
  }
}
```

The build copies the file to the output. A publish leaves it out.

| Setting | What it does | When the host cannot reach it |
|---|---|---|
| `AzureWebJobsStorage` | Names the storage account the host keeps its own state in. `UseDevelopmentStorage=true` is [Azurite](https://learn.microsoft.com/azure/storage/common/storage-connect-azurite) on its default ports, 10000 to 10002 | The host logs its storage health check as unhealthy every 20 seconds. The functions still run |
| `AzureWebJobsServiceBus` | Points the queue function at the Service Bus emulator on `localhost` | The host logs a `ConnectionRefused` error from the queue's receive loop about every 11 seconds. The function still runs from the admin endpoint |

[Queues](/azure/queue) covers the connection setting.

`POST /admin/functions/<function name>` asks the host to
[run a function that is not HTTP-triggered](https://learn.microsoft.com/azure/azure-functions/functions-manually-run-non-http).
The JSON body carries the trigger's input as the string `input`. The host answers 202 and runs the
function. A local host asks for no key. This exchange hands a message to `Queue_orders` while
`func start` runs:

```http
POST /admin/functions/Queue_orders
Content-Type: application/json

{"input":"{\"id\":\"A-1\",\"quantity\":2}"}

HTTP/1.1 202 Accepted
```

The worker's log lines appear in the `func start` output, with Hardened's request log among them.
This is the host's log for that invocation:

```text
Executing 'Functions.Queue_orders' (Reason='This function was programmatically called via the host APIs.', Id=c7c11363-d487-47ce-bd7e-dcb96746f88e)
QUEUE /orders started
QUEUE /orders  finished status code '(null)'  duration 00:00:00.0217282
Executed 'Functions.Queue_orders' (Succeeded, Id=c7c11363-d487-47ce-bd7e-dcb96746f88e, Duration=171ms)
```

The table shows what runs each function under `func start`:

| Function | Run it with |
|---|---|
| `Queue_orders`, `Topic_order_events` | The admin endpoint, with the message body as `input` |
| `Timer_nightly` | The admin endpoint, with the body `{}` |
| `Stream_clicks` | The admin endpoint, with the event body as `input` |
| `Event` | The admin endpoint, with the CloudEvent's JSON as `input` |
| `Blob_uploads` | The admin endpoint, with the blob's path, such as `uploads/2026/09/orders.csv`, as `input`. The blob has to exist in the storage account |
| `Change_orders` | A document written to the watched container, in the account or the emulator that `CosmosDB` names. [Changes](/azure/change) covers the emulator. The host disables the function when it cannot reach the account |
| `Http` | A request to the route's path |

The tests deliver to the handlers without a Functions host.
[Testing functions](/guide/testing-functions) and [Testing](/azure/testing) cover them.

## Failures and redelivery

On every trigger, a handler that throws fails the invocation. On a web route, the request answers
500 instead, and the invocation succeeds. A batch runs its items in order. The first item that
fails fails the invocation. The items after it do not run. [Triggers](/guide/triggers) covers
batches.

With `[ServiceBusModule(ReportsItemFailures = true)]`, every message of a batch runs. The worker
completes each message that succeeded. It abandons each message that failed. The invocation
succeeds. [Queues](/azure/queue) covers the setting.

| Trigger | When a handler throws | What happens next | Page |
|---|---|---|---|
| `[Get]` and the other verbs | The request answers 500 with the error body, and the invocation succeeds | The caller receives the 500 | [Web applications](/azure/web) |
| `[HardenedFunction]` | No adapter. The build fails with `HRDF001` | Not applicable | [Triggers](/guide/triggers) |
| `[Queue]` | The invocation fails at the first failed message, and the messages after it do not run. With `[ServiceBusModule(ReportsItemFailures = true)]`, every message runs, the failed ones are abandoned, the others are completed, and the invocation succeeds | The messages of a failed invocation are all [abandoned](https://learn.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger), the ones that ran included. Service Bus delivers an abandoned message again, and moves it to the [dead-letter queue](https://learn.microsoft.com/azure/service-bus-messaging/service-bus-dead-letter-queues) once its delivery count exceeds the queue's maximum delivery count, 10 by default | [Queues](/azure/queue) |
| `[Topic]` | As for `[Queue]` | As for `[Queue]`, with the subscription's maximum delivery count and dead-letter queue | [Topics](/azure/topic) |
| `[Timer]` | The invocation fails | The timer [does not retry](https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer). The function runs again at its next scheduled time | [Timers](/azure/timer) |
| `[Event]` | The invocation fails, and the host answers Event Grid with 500 | [Event Grid retries](https://learn.microsoft.com/azure/event-grid/delivery-and-retry) with backoff, up to 30 attempts within 24 hours by default. It then drops the event, or dead-letters it when the subscription has a dead-letter destination | [Events](/azure/event) |
| `[Change]` | The invocation fails at the first failed document, and the documents after it do not run | The trigger [does not deliver the batch again](https://learn.microsoft.com/azure/cosmos-db/troubleshoot-changefeed-functions). With a [retry policy](#retrying-a-failed-batch), the host invokes it again first | [Changes](/azure/change) |
| `[Stream]` | The invocation fails at the first failed event, and the events after it do not run | The trigger [advances its checkpoint](https://learn.microsoft.com/azure/azure-functions/functions-reliable-event-processing) past the batch, so the batch is not delivered again. With a [retry policy](#retrying-a-failed-batch), the host invokes it again first | [Streams](/azure/stream) |
| `[Blob]` | The invocation fails | The host [tries the blob five times](https://learn.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger) in all. It then writes a message naming the blob to the queue `webjobs-blobtrigger-poison`, in the storage account that the function's connection names | [Blobs](/azure/blob) |

::: warning
Without a retry policy, a `[Stream]` or `[Change]` handler that throws loses its batch. The trigger
moves past the batch. The failed invocation in the host's log is the only trace. Write `RetryCount`
and `RetryDelay` on the module for a source whose every event matters.
:::

## Retrying a failed batch

`[EventHubsModule]` and `[CosmosDbModule]` take a
[retry policy](https://learn.microsoft.com/azure/azure-functions/functions-bindings-error-pages).
`RetryCount` is how many times the host invokes a failed invocation again. `RetryDelay` is the wait
between attempts, as `hh:mm:ss`. This `src/Orders/Application.cs` sets a policy for a `[Stream]`
handler, in a function app that references `Hardened.Azure.Functions.EventHubs`:

```csharp
using Hardened.Azure.Functions.EventHubs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[EventHubsModule(RetryCount = 5, RetryDelay = "00:00:10")]
public partial class Application;
```

The policy covers every stream function, or every change function, in the application. The build
writes it as `[FixedDelayRetry(5, "00:00:10")]` on each function. It also writes it as the
function's `retry` block in the metadata that the host indexes. With this policy, a stream function
whose handler throws runs six times, ten seconds apart.

The Event Hubs trigger writes no checkpoint until the retries finish, so the partition waits for
the batch. When the policy is spent, the checkpoint moves past the batch. The host keeps the retry
count in the instance's memory. An instance that fails between attempts loses the count. The number
of retries is a best effort.

`RetryCount` and `RetryDelay` go together. Either one alone fails the build with `HRDAZ003`. The
error names the missing one. `RetryCount` takes an integer of 0 or more. Azure reads `-1` as
retrying without limit. A `RetryCount` of `-1` fails the build with `HRDAZ004`.

The build accepts a `RetryDelay` that does not parse as a time span, such as `"10s"`. At start, the
worker then fails to list its functions. The host runs none of them. This is the output of
`func start` with `RetryDelay = "10s"`:

```text
Worker failed to index functions
Result: Failure
Exception: System.FormatException: String '10s' was not recognized as a valid TimeSpan.
No job functions found. Try making your job classes and methods public. If you're using binding extensions (e.g. Azure Storage, ServiceBus, Timers, etc.) make sure you've called the registration method for the extension(s) in your startup code (e.g. builder.AddAzureStorage(), builder.AddServiceBus(), builder.AddTimers(), etc.).
```

The Service Bus, timer, Event Grid and blob modules have no retry setting.

## Deploying

Hardened has no deployment package for Azure. Its Azure packages are the ones under
[Packages](#packages). The function app runs on version 4 of the Functions runtime, with the
`dotnet-isolated` runtime and .NET 8. The template's projects target `net8.0`.

`dotnet publish -c Release` writes what the host runs: `Orders.dll` and the package assemblies,
`host.json`, `functions.metadata`, `worker.config.json`, `extensions.json` and the
`.azurefunctions` folder. `local.settings.json` is not among them.

[`az functionapp create`](https://learn.microsoft.com/azure/azure-functions/how-to-create-function-azure-cli)
creates the function app, with the runtime `dotnet-isolated`, the runtime version `8.0` and the
Functions version `4`. In `src/Orders`, `func azure functionapp publish orders` builds the project
and deploys it to the existing function app named `orders`:

```bash
az group create --name orders --location eastus
az storage account create --name ordersstorage --resource-group orders --sku Standard_LRS
az functionapp create --name orders --resource-group orders --storage-account ordersstorage \
    --consumption-plan-location eastus --os-type Windows \
    --runtime dotnet-isolated --runtime-version 8.0 --functions-version 4
cd src/Orders
func azure functionapp publish orders
```

A deployed function app reads its settings from the app's settings, not from `local.settings.json`.
`az functionapp config appsettings set` writes them. Each trigger page names the settings its
source needs. It also covers wiring the source to the function app. The trigger pages are
[Queues](/azure/queue), [Topics](/azure/topic), [Timers](/azure/timer), [Events](/azure/event),
[Changes](/azure/change), [Streams](/azure/stream) and [Blobs](/azure/blob).

[Environments](/guide/environments) covers naming the environment of a deployed function app.
[Web applications](/azure/web) covers a deployed `Http` function's address.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, batches, and what `HRDF001` and `HRDF003` mean |
| [Queues](/azure/queue) | Service Bus queues, and settling a batch message by message |
| [Streams](/azure/stream) | Event Hubs, with its connection and consumer group |
| [Web applications](/azure/web) | Web routes on Azure Functions |
| [Testing](/azure/testing) | Testing on Azure |
