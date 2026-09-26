# Triggers

A trigger attribute such as `[Queue]` on a method makes the method a handler for a source that is
not an HTTP request: a queue, a topic, a schedule, an event bus, a change feed, a stream or an
object store. The attribute names the source and no cloud.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The examples come from `dotnet new hardened-function -n Orders --trigger queue`. `Order` and
`OrderLog` are classes that the template writes in `src/Orders`.

The adapter package that the project references decides which cloud's source serves the trigger.
`src/Orders/Orders.csproj` references the adapter for Amazon SQS:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Sqs" Version="0.0.0-HARDENED-VERSION" />
```

With `Hardened.Aws.Lambda.Sqs` referenced, `[Queue("orders")]` receives the messages of the Amazon
SQS queue named `orders`. The handler runs once for each message, with the message bound to its
parameter.

The build registers the adapter's module. The application class in `src/Orders/Application.cs`
names no adapter:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

## Trigger attributes

Each attribute gives its handler a route:

| Attribute | Receives | Route |
|---|---|---|
| `[Queue(name)]` | Messages from a queue | `QUEUE /{name}` |
| `[Topic(name)]` | Messages published to a topic | `TOPIC /{name}` |
| `[Timer(name)]` | A schedule firing | `TIMER /{name}` |
| `[Event(source, detailType)]` | An event from an event bus | `EVENT /{source}/{detailType}` |
| `[Change(name)]` | A row or document that changed | `CHANGE /{name}` |
| `[Stream(name)]` | Records from a sharded stream | `STREAM /{name}` |
| `[Blob(name)]` | A notification that an object changed | `BLOB /{name}` |
| `[HardenedFunction]` or `[HardenedFunction(name)]` | A direct invocation, whose caller gets the handler's return value | `INVOKE /{name}`, or `INVOKE /{method name}` without a name |

The request log and the failure messages name a handler by its route, such as `QUEUE /orders`.

The attributes come from two packages:

| Attributes | Namespace | Package |
|---|---|---|
| `[Queue]`, `[Topic]`, `[Timer]`, `[Event]`, `[Change]`, `[Stream]`, `[Blob]` | `Hardened.Functions.Runtime.Attributes` | `Hardened.Functions.Runtime` |
| `[HardenedFunction]` | `Hardened.Requests.Abstract.Attributes` | `Hardened.Requests.Abstract` |

`Hardened.Functions.Runtime` references no cloud.

`[Event]` takes two arguments. `source` is who published the event. `detailType` is what happened.
This handler receives the `OrderPlaced` events that `com.acme.orders` publishes:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class PlacedHandler(OrderLog log)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnPlaced(Order order) => log.Record(order);
}
```

A `[Timer]` handler runs each time its schedule fires:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Timer("nightly")]
    public void OnNightly() => log.Sweep();
}
```

`[FunctionTesting]` delivers to trigger handlers in a test, through a façade the build generates for
each trigger kind. [Testing functions](/guide/testing-functions) covers testing each trigger.

## Adapter packages

Each cell names the source that serves the trigger on that cloud, then the adapter package.

| Trigger | AWS | Google Cloud | Azure |
|---|---|---|---|
| `[Queue]` | Amazon SQS, `Hardened.Aws.Lambda.Sqs` | A Pub/Sub push subscription, `Hardened.Gcp.CloudRun.PubSub` | A Service Bus queue, `Hardened.Azure.Functions.ServiceBus` |
| `[Topic]` | Amazon SNS, `Hardened.Aws.Lambda.Sns` | A Pub/Sub topic, through an Eventarc trigger, `Hardened.Gcp.CloudRun.PubSub` | A Service Bus topic, `Hardened.Azure.Functions.ServiceBus` |
| `[Timer]` | An EventBridge rule on a schedule, `Hardened.Aws.Lambda.EventBridge` | A Cloud Scheduler job, `Hardened.Gcp.CloudRun.Scheduler` | A timer trigger, `Hardened.Azure.Functions.Timer` |
| `[Event]` | An EventBridge event, `Hardened.Aws.Lambda.EventBridge` | An Eventarc CloudEvent, `Hardened.Gcp.CloudRun.Eventarc` | An Event Grid event, `Hardened.Azure.Functions.EventGrid` |
| `[Change]` | DynamoDB Streams, `Hardened.Aws.Lambda.DynamoDb` | Firestore, through Eventarc, `Hardened.Gcp.CloudRun.Firestore` | The Cosmos DB change feed, `Hardened.Azure.Functions.CosmosDb` |
| `[Stream]` | Kinesis Data Streams, `Hardened.Aws.Lambda.Kinesis` | No adapter. The build fails with `HRDF001` | Event Hubs, `Hardened.Azure.Functions.EventHubs` |
| `[Blob]` | S3 object notifications, `Hardened.Aws.Lambda.S3` | Cloud Storage, `Hardened.Gcp.CloudRun.Storage` | Blob Storage, `Hardened.Azure.Functions.Blobs` |
| `[HardenedFunction]` | A direct invocation, `Hardened.Aws.Lambda.Invoke` | A POST to the service, `Hardened.Gcp.CloudRun.Invoke` | No adapter. The build fails with `HRDF001` |

Moving a handler to another cloud changes the packages and the entry point. The handler does not
change. On Google Cloud, the application class that the `hardened-function` template writes also
carries `[CloudRunRuntime]`.

### Cloud pages

Each cloud's overview covers its packages and entry point. Each cloud's trigger pages cover routing,
headers, settings and failures for that cloud.

| Cloud | Overview | Trigger pages |
|---|---|---|
| AWS | [Overview](/aws/) | [Invocations](/aws/invoke), [Queues](/aws/queue), [Topics](/aws/topic), [Timers](/aws/timer), [Events](/aws/event), [Changes](/aws/change), [Streams](/aws/stream), [Blobs](/aws/blob) |
| Google Cloud | [Overview](/gcp/) | [Invocations](/gcp/invoke), [Queues](/gcp/queue), [Topics](/gcp/topic), [Timers](/gcp/timer), [Events](/gcp/event), [Changes](/gcp/change), [Blobs](/gcp/blob) |
| Azure | [Overview](/azure/) | [Queues](/azure/queue), [Topics](/azure/topic), [Timers](/azure/timer), [Events](/azure/event), [Changes](/azure/change), [Streams](/azure/stream), [Blobs](/azure/blob) |

## Build properties and modules

Each adapter package's build file sets an MSBuild property to the full name of its module class.
The build reads the property of every trigger that the project's handlers use. On the application
class, it registers the module that each property names. The registration is in the generated
`Application.TriggerModules.cs`, under `obj/Debug/net8.0/generated/Hardened.Library.SourceGenerator/`.

The table names the property that each trigger reads, and the module that each cloud's package sets
it to:

| Trigger | Build property | AWS module | Google Cloud module | Azure module |
|---|---|---|---|---|
| `[Queue]` | `HardenedQueueModule` | `Hardened.Aws.Lambda.Sqs.SqsModule` | `Hardened.Gcp.CloudRun.PubSub.PubSubModule` | `Hardened.Azure.Functions.ServiceBus.ServiceBusModule` |
| `[Topic]` | `HardenedTopicModule` | `Hardened.Aws.Lambda.Sns.SnsModule` | `Hardened.Gcp.CloudRun.PubSub.PubSubModule` | `Hardened.Azure.Functions.ServiceBus.ServiceBusModule` |
| `[Timer]` | `HardenedTimerModule` | `Hardened.Aws.Lambda.EventBridge.EventBridgeModule` | `Hardened.Gcp.CloudRun.Scheduler.SchedulerModule` | `Hardened.Azure.Functions.Timer.TimerModule` |
| `[Event]` | `HardenedEventModule` | `Hardened.Aws.Lambda.EventBridge.EventBridgeModule` | `Hardened.Gcp.CloudRun.Eventarc.EventarcModule` | `Hardened.Azure.Functions.EventGrid.EventGridModule` |
| `[Change]` | `HardenedChangeModule` | `Hardened.Aws.Lambda.DynamoDb.DynamoDbStreamsModule` | `Hardened.Gcp.CloudRun.Firestore.FirestoreModule` | `Hardened.Azure.Functions.CosmosDb.CosmosDbModule` |
| `[Stream]` | `HardenedStreamModule` | `Hardened.Aws.Lambda.Kinesis.KinesisModule` | None | `Hardened.Azure.Functions.EventHubs.EventHubsModule` |
| `[Blob]` | `HardenedBlobModule` | `Hardened.Aws.Lambda.S3.S3Module` | `Hardened.Gcp.CloudRun.Storage.StorageModule` | `Hardened.Azure.Functions.Blobs.BlobsModule` |
| `[HardenedFunction]` | `HardenedInvokeModule` | `Hardened.Aws.Lambda.Invoke.InvokeModule` | `Hardened.Gcp.CloudRun.Invoke.InvokeModule` | None |
| `[Get]` and the other verbs | `HardenedHttpModule` | `Hardened.Aws.Lambda.Http.LambdaHttpModule` | `Hardened.Gcp.CloudRun.Runtime.CloudRunRuntime` | `Hardened.Azure.Functions.Http.HttpModule` |

When one module serves two triggers, the build registers it once. The build does not register a
module that the application class already applies as an attribute. The attribute's settings stay as
written.

The property's value is the full type name of a module. The build writes `new` of that type into
the registration. A property set in the project file keeps its value. The package sets the property
only when it is empty.

These packages make the properties visible to the build:

| Package | Properties |
|---|---|
| `Hardened.Functions.Runtime` | The seven trigger properties |
| `Hardened.Requests.Abstract` | `HardenedInvokeModule` |
| `Hardened.Web.Runtime` | `HardenedHttpModule` |

`[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]` all bind `HardenedHttpModule`. A project with
routes registers one HTTP adapter. [Hosts](/guide/hosts) covers web hosts.

`Hardened.Aws.Lambda`, `Hardened.Gcp.CloudRun` and `Hardened.Azure.Functions` each reference every
adapter of their cloud. Each of them sets every property at once.

### Handlers in a library project

The build registers adapters only for handlers compiled in the same project as the application
class. When the handlers are in a library project, the application class applies the adapter's
module itself, such as `[SqsModule]`. Without the module, a Lambda function stops at startup with
"No service for type 'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been
registered."

In this application, the handlers are in the library `Orders.Handlers`, whose module is
`OrdersLibrary`:

```csharp
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Runtime.Attributes;
using Orders.Handlers;

namespace Orders;

[HardenedModule]
[SqsModule]
[OrdersLibrary]
public partial class Application;
```

The library project references `Hardened.Shared.Runtime`, `Hardened.Requests.Runtime`,
`Hardened.Functions.Runtime`, `Hardened.Library.SourceGenerator` and
`Hardened.Function.SourceGenerator`. It holds a `[HardenedModule]` class. It references no cloud
package.

In this layout, the application's own project does not reference
`Hardened.Function.SourceGenerator`. With that reference, the application gets a second, empty
handler table. The first invocation then fails with "This container holds two applications, so
there are two handler tables and nothing says which one a message routes through."

## Source names

Each attribute takes the source's own name, such as a queue's name. It does not take an ARN, a URL
or a connection string. On AWS and Google Cloud, the adapter reads the name from the delivery and
routes on it. On Azure, the build writes one function for each source, bound to the source by that
name. For `[Queue("orders-q")]` it writes the function `Queue_orders_q`, with
`ServiceBusTrigger("orders-q", IsBatched = true)` and the route `QUEUE /orders-q`. Each cloud's
[trigger page](#cloud-pages) says which part of the delivery holds the name.

Names match exactly, including case. The route includes the scheme. A queue and a topic with the
same name are two routes.

A delivery from a source that no handler names fails. For a queue named `unknown-queue`, the
message is "No handler is registered for QUEUE /unknown-queue. An event source is wired to this
function that no trigger attribute declared." On Lambda the invocation fails. On Cloud Run the
request answers 500. When the application has
[a handler named like its method](#a-handler-named-like-its-method), that handler receives such a
delivery instead.

## Payload binding

The handler's parameter that is not a service binds the payload, read as JSON. Payload property
names match without regard to case. A parameter whose type is an interface, or a class registered
as a service, comes from the container. [Parameter binding](/guide/parameter-binding) covers the
rules.

The payload depends on the trigger:

| Trigger | The payload |
|---|---|
| `[Queue]`, `[Topic]` | The message body |
| `[Timer]` | Usually empty. The handler can take no payload parameter |
| `[Event]` | The event's data, not the bus envelope. On EventBridge it is the `detail` field |
| `[Change]` | The row or document that changed. [Change feeds](#change-feeds) compares the three clouds |
| `[Stream]` | The record's data, as the publisher wrote it |
| `[Blob]` | A notification that describes the object. The object itself is not in it |
| `[HardenedFunction]` | The caller's payload |

A `string` parameter binds a payload that is a JSON string. A plain-text payload fails to bind with
`JsonException`. A `byte[]` parameter receives the payload's bytes as they arrived. A payload that
fails to bind is a failed item, as a thrown exception is.

A parameter of type `IExecutionRequest` gives the delivery's scheme as `Method`, its route as
`Path`, and its metadata as `Headers`. The interface is in `Hardened.Requests.Abstract.Execution`.
Each cloud's [trigger page](#cloud-pages) lists the headers its adapter sets.

`[FromHeader]` does not bind on a trigger handler. With `Hardened.Web.Runtime` referenced, the build
succeeds. Every delivery then fails with "Attribute type
Hardened.Web.Runtime.Attributes.FromHeaderAttribute does not implement ICustomBindingAttribute".

### Blob notifications

A blob handler binds a notification with the fields below. `bucket`, `key` and `size` have the same
names on all three clouds. A type with those properties binds on each.

| Field | AWS (S3) | Google Cloud (Cloud Storage) | Azure (Blob Storage) |
|---|---|---|---|
| `bucket` | The bucket | The bucket | The container |
| `key` | The object's key, URL-decoded | The object's name | The blob's name |
| `size` | Bytes. Absent on a delete | Bytes, when the metadata carries it | Bytes, when the host sends it |
| The other fields | `eTag`, `sequencer`, `eventName`, `eventTime` | `name`, `contentType`, `generation`, `etag`, `eventType`, `eventTime`, `timeCreated`, `updated` | `container`, `name`, `contentType`, `eTag`, `uri`, `eventName` |

This type binds on all three clouds:

```csharp
namespace Orders;

public class Upload
{
    public string Bucket { get; set; } = "";

    public string Key { get; set; } = "";

    public long Size { get; set; }
}
```

A handler takes it as its parameter:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Blob("uploads")]
    public void OnUpload(Upload upload) => log.Record(upload);
}
```

To read the object, the handler fetches it from the store with the cloud's own client.

## Batches and failed items

Some sources deliver several items at once. The handler runs once for each item, in the order the
source delivered them. By default, the first failed item fails the whole delivery with the
handler's exception. The items after it do not run.

A source that can report failed items one by one does so only when the application turns reporting
on. With reporting on, the source's `BatchFailureMode` decides what happens after a failed item.
The type is in `Hardened.Requests.Abstract.Execution`. The table shows each mode when the second of
three items fails:

| Mode | Items that run | What the source is told |
|---|---|---|
| `PerItem` | All three | The failed item |
| `Checkpoint` | The first two | The failed item, the position the source resumes from |

The next table shows how each source delivers, and the module setting that turns reporting on:

| Trigger | AWS | Google Cloud | Azure |
|---|---|---|---|
| `[Queue]` | A batch, `PerItem`. Reported with `[SqsModule(ReportBatchItemFailures = true)]` | One message per request | A batch, `PerItem`. Reported with `[ServiceBusModule(ReportsItemFailures = true)]` |
| `[Topic]` | A batch, `PerItem`. Never reported | One message per request | A batch, `PerItem`. Reported with `[ServiceBusModule(ReportsItemFailures = true)]` |
| `[Timer]`, `[Event]` | One per invocation | One per request | One per invocation |
| `[Change]` | A batch, `Checkpoint`. Reported with `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]` | One event per request | A batch, `Checkpoint`. Never reported |
| `[Stream]` | A batch, `Checkpoint`. Reported with `[KinesisModule(ReportBatchItemFailures = true)]` | No adapter | A batch, `Checkpoint`. Never reported |
| `[Blob]` | A batch, `PerItem`. Never reported | One notification per request | One blob per invocation |
| `[HardenedFunction]` | One payload per invocation | One request per invocation | No adapter |

This application class turns reporting on for Amazon SQS:

```csharp
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

Each cloud's [overview and trigger pages](#cloud-pages) say what the cloud does with a failed
delivery.

## Filters on trigger handlers

A trigger handler runs through the execution pipeline, as a route does.
[The execution pipeline](/guide/execution-pipeline) covers the filters.

With `ValidationModules.SourceGenerator` referenced, the pipeline checks the constraint attributes
and rules classes for a trigger's payload type before the handler runs. Without that package, the build warns with
`HRDV006`. The pipeline then checks nothing. [Validation](/guide/validation) covers the
constraints.

A refused item counts as a failed item. By default, the delivery fails with `ValidationException`.
The exception's message is "One or more validation errors occurred." The items after the refused
item do not run.

`[Retry]` retries a trigger handler only with `AllowNonIdempotent = true`. A trigger's request
method is its scheme, such as `QUEUE`, which is not in the list of methods the attribute treats as
idempotent. In a batch, the attribute retries each item on its own.

## Change feeds

`[Change]` behaves differently on each cloud:

| | AWS: DynamoDB Streams | Google Cloud: Firestore | Azure: Cosmos DB change feed |
|---|---|---|---|
| `[Change(name)]` names | The table | The collection the document is in | The container |
| The parameter binds | On a stream that carries new images, the item after the change, without DynamoDB's type wrappers. On a `REMOVE`, the item before it. [Changes](/aws/change) covers the other stream views | The document after the change, as JSON. On a delete, the document as it was | The document as it is now, with Cosmos DB's system properties such as `_etag` and `_ts` |
| The previous version | `[OldImage]`, in DynamoDB's own form. Null on an `INSERT`, and when the stream does not carry old images | `[OldValue]`, as the handler's type or as Firestore's `Document`. Null on a create | Not available |
| The kind of change | Header `x-amz-ddb-event-name`: `INSERT`, `MODIFY` or `REMOVE` | Header `ce-type`, ending in `created`, `updated`, `deleted` or `written` | No header |
| Delivery | A batch, `Checkpoint` | One event per request | A batch, `Checkpoint` |
| Failed items reported | With `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]` | Not applicable | Never |
| After a handler throws | Without reporting, the invocation fails and Lambda retries the batch. With reporting, the invocation names the failed record and Lambda resumes from it | The request answers 500 | The invocation fails. The feed moves past the batch unless `[CosmosDbModule]` sets `RetryCount` and `RetryDelay` |

`[OldImage]` and `[NewImage]` are in `Hardened.Aws.Lambda.DynamoDb`. `[OldValue]` is in
`Hardened.Gcp.CloudRun.Firestore`. The [Changes](/gcp/change) page for Google Cloud and the
[Changes](/azure/change) page for Azure cover the settings.

## Missing and unused adapters

A project whose handlers use a trigger that no referenced package binds fails to build with the
error `HRDF001`. The build reports it when a referenced package binds some other trigger. For a
`[Queue]` handler, the error reads:

```text
error HRDF001: Handlers in this project use [Queue], but no referenced runtime declares a module for it. Reference a runtime package that supports Queue triggers, or set <HardenedQueueModule> to the module that should serve them.
```

`HRDF001` also reports the two triggers a cloud has no adapter for: `[Stream]` on Google Cloud and
`[HardenedFunction]` on Azure. With only `Hardened.Gcp.CloudRun.Runtime` referenced, a project with
a `[Stream]` handler fails with:

```text
error HRDF001: Handlers in this project use [Stream], but no referenced runtime declares a module for it. Reference a runtime package that supports Stream triggers, or set <HardenedStreamModule> to the module that should serve them.
```

With `Hardened.Azure.Functions.Runtime` and `Hardened.Azure.Functions.ServiceBus` referenced, a
project with a `[HardenedFunction]` handler fails with:

```text
error HRDF001: Handlers in this project use [HardenedFunction], but no referenced runtime declares a module for it. Reference a runtime package that supports HardenedFunction triggers, or set <HardenedInvokeModule> to the module that should serve them.
```

When no referenced package binds any trigger, the build reports nothing. The build succeeds for a
Lambda function that references `Hardened.Aws.Lambda.Runtime` and no adapter package. The function
stops at startup with "No service for type
'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been registered." An Azure function that references `Hardened.Azure.Functions.Runtime` and no adapter
package fails to build in `Program.cs`, with `CS1929` on `UseHardened`.

The build writes `HRDF003` for each bound module that serves no trigger the project uses. Its
severity is Info. The diagnostic names the module, its triggers and its properties. A module that
serves two triggers gets one `HRDF003`, and only when the project uses neither. `dotnet build`
prints `HRDF003` at `-v:d` and above, and not at the default verbosity. In a queue function that
references `Hardened.Aws.Lambda`, `dotnet build -v:d` prints six `HRDF003` lines. This is one of
them:

```text
info HRDF003: Hardened.Aws.Lambda.EventBridge.EventBridgeModule is bound to serve [Timer] and [Event] and nothing in this project declares one, so it ships in the deployment bundle unreachable. Reference the adapter packages for the triggers this project uses rather than a meta package, or clear <HardenedTimerModule> and <HardenedEventModule>.
```

[Diagnostics](/reference/diagnostics) lists every code.

## A handler named like its method

The build compares each handler's route name with its method's name, including case. The route
name is the source's name, or a `[HardenedFunction]`'s name. When the two are the same, the handler
receives every delivery that no other handler's route matches, whatever its scheme. A
`[HardenedFunction]` without a name is such a handler. With two such handlers, one of them receives
all those deliveries. The other receives none.

A different case is a different name. `[Timer("nightly")]` on `OnNightly` or on `Nightly` is routed
by its name.

::: warning
Give a trigger's source a name that differs from its method's name. `[Queue("Audit")]` on a method
named `Audit` receives every delivery that matches no other handler, including messages from other
sources. The invocation succeeds.
:::

## Limits

Two sources of one kind whose names differ only in case, such as `[Queue("orders")]` and
`[Queue("ORDERS")]`, stop the function generator. The build succeeds with the warning `CS8785`,
"The hintName 'QUEUE.orders.FunctionHandler.cs' of the added source file must be unique within a
generator". The generator writes no handler. A Lambda function built that way fails each invocation
with "This function declares no handlers. A verb attribute or a trigger attribute on a method is
what compiles one."

On a source that delivers a batch, a handler whose constructor needs a service that nothing
registers fails each delivery. For `OrderHandler`, the message is "HandlerInstance was not instance
of Orders.OrderHandler". The message does not name the missing service.

## Next

- [Testing functions](/guide/testing-functions): testing trigger handlers
- AWS [Overview](/aws/): the AWS packages, the application class and the Lambda entry point
- Google Cloud [Overview](/gcp/): the Google Cloud packages and host
- Azure [Overview](/azure/): the Azure packages and host
- [The execution pipeline](/guide/execution-pipeline): the filters a trigger handler runs through
