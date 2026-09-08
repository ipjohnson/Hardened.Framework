# Testing Azure handlers

Every Azure handler is tested in-process, through the real pipeline. There is no deployment, no
Azure subscription in the loop, and no Functions host until the last rung.

```csharp
[HardenedTest]
public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
    await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
}
```

The shape is the same as [any Hardened test](/guide/testing): assembly attributes install the
harness and name the application, and the test method takes what it needs.

## Three rungs

Fidelity is a property of the project, or of a class, and never of a test method. The same test
runs at every rung.

| Rung | Turned on by | What runs |
|---|---|---|
| Pipeline | `[assembly: FunctionTesting]` | The message is built as a request and the pipeline runs: routing, binding, the filters and the handler. Names no cloud |
| Worker | `[assembly: AzureFunctionsTesting]` beside it | The trigger data the isolated worker would bind - a `ServiceBusReceivedMessage[]`, an `EventData[]`, the timer's JSON, a change feed's array, a `BlobClient`, a CloudEvent - and the binding data the host sends beside it, handed to the real invocation handler, so the adapter, the batch fan-out and the settlement run too |
| Container | A project in the simulator solution | The build output inside `mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0`, indexed by the real host from the generated provider and driven by the Service Bus or Event Hubs emulator, observed through what the worker prints |

The first two are in the fixture's test project and run with `dotnet test`. The third is a
project of its own that needs Docker, and it is in the repository rather than in a package; see
[the container tier](#the-container-tier).

```csharp
// Bootstrap.cs
using Hardened.Azure.Functions.Testing;
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;

[assembly: FunctionTesting]
[assembly: AzureFunctionsTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`[FunctionTesting]` makes the generated [trigger façades](/guide/triggers#testing) resolvable.
`[AzureFunctionsTesting]` replaces the delivery behind them. Nothing in a test starts the
Functions host, and the worker's own converter - the part that turns the host's bytes into the
SDK's type - is exercised only in the container tier.

**No test method changes when a line is added or removed.** Delete `[AzureFunctionsTesting]` to
drop back to the pipeline, or keep it and mark one class `[PipelineDelivery]` to run that class
through the pipeline alone; `[PipelineDelivery]` is in `Hardened.Functions.Testing`.

Neither attribute cares which order it is applied in. The neutral delivery registers with `TryAdd`
and the Azure one replaces it, so both orders end with the worker delivery.

The repository's fixtures compile one test file into two projects, one per rung, so a test that
passes on the pipeline and not through the worker names a defect in the adapter, and the reverse
names one in what the worker hands over.

## The façades

One nested class per trigger kind on the entry point, with a method per source:

| Trigger | Façade | A method named for the |
|---|---|---|
| `[Queue]` | `Application.Queues` | queue |
| `[Topic]` | `Application.Topics` | topic |
| `[Timer]` | `Application.Timers` | schedule |
| `[Change]` | `Application.Changes` | container |
| `[Stream]` | `Application.Streams` | hub |
| `[Blob]` | `Application.Blobs` | container |

`[Event]` has no façade, because it is addressed by source and type where every other trigger has
one name. A test sends through `ITriggerDelivery` itself, and both rungs route it:

```csharp
[HardenedTest]
public async Task AnEventReachesItsHandler(ITriggerDelivery delivery, [Mock] ITriggerLog log) {
    await delivery.Deliver([new Order { Id = "e-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

    log.Received().Record("event:e-1");
}
```

A method exists because the handler does, and its parameter is the type the handler binds. A
renamed source or a changed payload is a compile error in the test rather than a test that quietly
passes against nothing. `[Queue("orders-new")]` becomes `queues.OrdersNew(...)`, and passing
several payloads sends one batch of them.

There is no `Application.Invocations`, because there is no direct invoke on Azure; see
[Azure Functions](/azure/#the-packages).

## What each delivery is

Under `[AzureFunctionsTesting]` the delivery builds what the worker would have bound for the
scheme the handler declared, through each SDK's own model factory where one exists:

| Scheme | The trigger data |
|---|---|
| `QUEUE`, `TOPIC` | A `ServiceBusReceivedMessage[]` from `ServiceBusModelFactory`, one message per payload, each on its first delivery with the JSON body, a message id, a lock token and a sequence number; the parallel binding-data arrays the host sends beside a batch |
| `TIMER` | The timer's JSON: the schedule, its last and next occurrence, and `IsPastDue` false. One invocation per façade call |
| `STREAM` | An `EventData[]` from `EventHubsModelFactory`, in order, with ascending sequence numbers and offsets |
| `CHANGE` | One JSON array of documents, each with `_lsn`, `_ts` and `_etag` stamped on it beside the payload's own properties |
| `BLOB` | A `BlobClient` for the blob's URI, which costs no request, and the `Properties` the host sends beside it, with the payload's `name` and `size`. One invocation per payload |
| `EVENT` | A structured CloudEvent whose source and type come from the route, with the payload as its data |

A handler that throws fails the invocation, which the test sees as the handler's own exception.
With settlement on, it is settled instead; see below.

## Settlement

A function whose application says `[ServiceBusModule(ReportsItemFailures = true)]` completes and
abandons each message itself, through the `ServiceBusMessageActions` the worker binds from the
host. A test has no host, so `RecordingMessageActions` stands in and records what was settled, by
message id:

```csharp
var actions = new RecordingMessageActions();

await handler.Invoke(
    new FunctionsTrigger("QUEUE", "/orders", new ServiceBusDelivery(messages, actions)),
    new TestFunctionContext("Queue_orders", new Dictionary<string, object?>(), provider));

Assert.Equal(["m-a-2"], actions.Abandoned);
Assert.Equal(["m-a-1", "m-a-3"], actions.Completed);
```

Registered as `ServiceBusMessageActions` in the container, the delivery picks it up for every
batch it builds. Without a channel, the adapter falls back to the default: a failure fails the
invocation.

## Web handlers

An Azure web application's routes are ordinary routes, so [`ITestWebApp`](/guide/testing-web)
drives them with no Azure involvement. `[AzureFunctionsWebTesting]` beside `[WebTesting]` raises
the host: the request is built as the worker's own `HttpRequestData`, with the path the host's
catch-all route would have matched in the binding data, and goes through the real invocation
handler, the HTTP adapter and the routing table, and the answer comes back as `HttpResponseData`.
No Functions host process is started.

```csharp
[assembly: WebTesting]
[assembly: AzureFunctionsWebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

## What the host is told

`MetadataAgreement.Disagreements(provider)` compares the generated metadata provider with the
`functions.metadata` the Worker SDK's build task wrote from the same shims, binding for binding,
as JSON rather than text. An empty list is agreement; every fixture in the repository asserts it,
and a function app's own test project can:

```csharp
[Fact]
public async Task TheProviderAndTheBuildTaskAgree() {
    Assert.Empty(await MetadataAgreement.Disagreements(new ApplicationAzureFunctionMetadataProvider()));
}
```

## Substituting a dependency

`[Mock]` works as it does anywhere else, so a handler's collaborator is a test parameter:

```csharp
[HardenedTest]
public async Task AnOrderIsPlaced(Application.Queues queues, [Mock] IOrderStore store) {
    await queues.Orders(new Order { Id = "a-1" });

    store.Received().Place(Arg.Is<Order>(order => order.Id == "a-1"));
}
```

The mock library is one package and one assembly attribute; see
[Substituting services](/guide/testing-mocks).

## The container tier

The last rung runs the fixture the way Azure runs it: the build output mounted into the Functions
host image at `/home/site/wwwroot`, on a Testcontainers network with Azurite as the storage
account the host requires and the Service Bus emulator, with the SQL Server it keeps its state in,
or the Event Hubs emulator as the source. The host indexes the functions from the generated
provider, which the test reads off the host's log, and a message published to the emulator through
the SDK's own client reaches the handler, which the test reads off the container's output.

What the tier proves is the whole deployed path: the host's extension receives the message, the
worker's converter turns it into the SDK's type, the generated executor and shim run it through
the adapter, the batch filter and the binder, and the host records the invocation succeeded or
failed. A handler that throws on a queue sees the same message again, which is the redelivery
the queue's delivery count allows.

These projects live in `Hardened.Simulators.slnx` in the repository, not in a package, and they
need a Docker daemon. On a machine without one they fail at container startup rather than
skipping. The host image is published for amd64 only, so an Apple Silicon machine runs it under
emulation.

## Next

- [Triggers](/guide/triggers): the façades, and what each source delivers
- [Writing a test](/guide/testing): the harness this builds on
- [Test hosts](/guide/testing-hosts): the seam `[AzureFunctionsWebTesting]` plugs into
