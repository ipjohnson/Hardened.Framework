# Azure Functions

The handlers you wrote for Kestrel, for Lambda or for Cloud Run run on Azure Functions. What
changes is a package reference.

```csharp
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
public partial class Application;

public class OrderHandler(OrderLog log) {

    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The application names nothing: no host attribute and no adapter. The handler carries a
[trigger](/guide/triggers); the generator reads the build property the adapter package declares,
registers the module for you, and writes the function the Functions host indexes - one
`[Function]` per source, with the extension's own trigger attribute on it, in
`obj/.../generated/Hardened.Azure.Functions.SourceGenerator/`. Nothing in the application says
Service Bus, so moving the queue to another provider changes a package reference.

`dotnet new hardened-function --host azure` and `dotnet new hardened-web --host azure-functions`
write both shapes with tests; see [Project templates](/guide/project-templates).

## The packages

One runtime package, and one adapter per source. An adapter is a package rather than a flag so a
function app carries only the worker extension it can reach, and only that extension's WebJobs
counterpart ends up in the host. Each adapter references the worker extension for its source and
nothing else of Azure's; the extension is what carries the trigger attribute the generated
function wears and the converter that turns the host's bytes into the SDK's own type.

| Package | Serves |
|---|---|
| `Hardened.Azure.Functions.Runtime` | The worker registration, `UseHardened<T>()`, the invocation handler and the request shapes. Every Azure application references it, beside `Microsoft.Azure.Functions.Worker.Sdk` |
| `Hardened.Azure.Functions.ServiceBus` | `[Queue]` from a Service Bus queue and `[Topic]` from a subscription, with per-message settlement |
| `Hardened.Azure.Functions.Timer` | `[Timer]` from a timer trigger whose schedule is an app setting |
| `Hardened.Azure.Functions.EventHubs` | `[Stream]` from Event Hubs |
| `Hardened.Azure.Functions.CosmosDb` | `[Change]` from the Cosmos DB change feed |
| `Hardened.Azure.Functions.Blobs` | `[Blob]` from Blob Storage, fed by Event Grid |
| `Hardened.Azure.Functions.EventGrid` | `[Event]` from Event Grid, in the CloudEvents schema |
| `Hardened.Azure.Functions.Http` | The web verbs, behind one anonymous HTTP trigger |
| `Hardened.Azure.Functions` | Every adapter in one reference. `HRDF003` says which ones the function app does not use |
| `Hardened.Azure.Functions.SourceGenerator` | The generator that writes the functions, the metadata provider and the executor |
| `Hardened.Azure.Functions.Testing` | `[AzureFunctionsTesting]` and `[AzureFunctionsWebTesting]`, the deliveries that build what the worker would bind |

There is no `[HardenedFunction]` adapter, and there is not going to be one in this line. Azure
Functions has no direct invocation of a function: a caller reaches a function app over HTTP, and
an HTTP route is a web application, which is what `hardened-web --host azure-functions` writes.
A project that writes `[HardenedFunction]` and references only these packages fails the build with
`HRDF001`, which names the gap. See
[When nothing serves a trigger](/guide/triggers#when-nothing-serves-a-trigger).

## What the runtime gives you

**The worker.** An Azure function app is an isolated worker the Functions host starts and speaks
to over gRPC. `UseHardened<Application>()` inside `ConfigureFunctionsWorkerDefaults` registers
the application and the generated pair the host needs: a metadata provider, which is what the
host indexes when it asks the worker for its functions, and an executor, which is what runs when
the host invokes one. Both are generated from the handlers, so the host is told about exactly the
functions the handlers declare, and nothing is found by reflection.

**One function per source.** `[Queue("orders")]` becomes a function named `Queue_orders` wearing
the Service Bus extension's trigger attribute. A function's shim hands the invocation handler the
trigger data the worker bound, the route the handler was compiled under, and which dispatch the
family uses: a trigger goes to the function table and the HTTP function to the web table, chosen
per invocation, so one function app serves `[Get]` routes and `[Queue]` handlers together.

**Deployment facts on the module.** A topic is read through a subscription, a Cosmos container
lives in a database, a connection is an app setting; none of those has a slot on the neutral
trigger. They are written once on the application, `[ServiceBusModule(Subscription = "...")]`,
and the generator writes them into the function's binding. One that a binding needs and the
module did not supply is `HRDAZ003` at build, naming the property and where to write it.

**What the host is told agrees with what the build wrote.** `Microsoft.Azure.Functions.Worker.Sdk`
scans the generated functions into `functions.metadata` beside the generated provider, and the
testing package's `MetadataAgreement` says whether the two describe the same functions.

**Trimming and Native AOT.** Every runtime package passes the trim and AOT analyzers. The
repository publishes a worker with `PublishAot` as the gate, and a trimmed, self-contained worker
that the real host indexes and invokes. The worker package itself cannot start as a native
binary, for a reason the [design page](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/design/azure/application-types.md#native-aot-and-trimming)
gives, so a function app on this line ships trimmed rather than native.

## What a failure means

Every family fails the invocation when a handler throws, and what the host does with a failed
invocation is the source's own rule:

| Family | After a failed invocation |
|---|---|
| Queue, topic | The batch is abandoned and the queue delivers it again, up to its delivery count. With settlement on, only the failed messages are abandoned; see [Queues](/azure/queue) |
| Timer | The run is recorded as failed and the schedule fires again next time |
| Blob | The trigger retries the blob and, in the end, records it as poison |
| Event | Event Grid retries the delivery and dead-letters it |
| Stream, change | **The batch is not delivered again.** The host advances the checkpoint or lease when the invocation completes, failed or not, unless the function app declares a retry policy; see [Streams](/azure/stream) and [Changes](/azure/change) |

The last row is where Azure differs from Kinesis and DynamoDB Streams, and it is written on both
pages rather than smoothed over.

## Deploying

There is no infrastructure package. A function app is created with `az` and the worker is
published into it with Core Tools:

```bash
az functionapp create --name orders --resource-group orders --storage-account ordersstorage \
    --consumption-plan-location eastus --runtime dotnet-isolated --functions-version 4
func azure functionapp publish orders
```

Each family's page shows the app setting that wires its source to the function app. The
templates write `host.json` and `local.settings.json`, and `func start` runs the same host
locally.

## Where things are

| Area | Page | Source |
|---|---|---|
| The worker, the generated functions and the HTTP routes | [Web applications](/azure/web) | [`Hardened.Azure.Functions.Runtime`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.Runtime), [`Hardened.Azure.Functions.Http`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.Http) |
| Queues | [Queues](/azure/queue) | [`Hardened.Azure.Functions.ServiceBus`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.ServiceBus) |
| Topics | [Topics](/azure/topic) | [`Hardened.Azure.Functions.ServiceBus`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.ServiceBus) |
| Schedules | [Timers](/azure/timer) | [`Hardened.Azure.Functions.Timer`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.Timer) |
| Events | [Events](/azure/event) | [`Hardened.Azure.Functions.EventGrid`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.EventGrid) |
| Documents | [Changes](/azure/change) | [`Hardened.Azure.Functions.CosmosDb`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.CosmosDb) |
| Streams | [Streams](/azure/stream) | [`Hardened.Azure.Functions.EventHubs`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.EventHubs) |
| Blobs | [Blobs](/azure/blob) | [`Hardened.Azure.Functions.Blobs`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.Blobs) |
| Test harnesses | [Testing Azure handlers](/azure/testing) | [`Hardened.Azure.Functions.Testing`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.Testing) |
| The generator | [Application types](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/design/azure/application-types.md) | [`Hardened.Azure.Functions.SourceGenerator`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Azure/Hardened.Azure.Functions.SourceGenerator) |

The trigger attributes themselves are not here. They live in `Hardened.Functions.Runtime`, which
names no cloud; see [Triggers](/guide/triggers). The CloudEvents reader the Event Grid adapter
shares with Eventarc is `Hardened.CloudEvents`, which names no cloud either.
