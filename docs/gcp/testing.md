# Testing Cloud Run handlers

Every Cloud Run handler is tested in-process, through the real pipeline. There is no deployment and
no Google account in the loop, and no emulator until the last rung.

```csharp
[HardenedTest]
public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
    await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
}
```

The shape is the same as [any Hardened test](/guide/testing): assembly attributes install the
harness and name the application, and the test method takes what it needs.

## Four rungs

Fidelity is a property of the project, or of a class, and never of a test method. The same test
runs at every rung.

| Rung | Turned on by | What runs |
|---|---|---|
| Pipeline | `[assembly: FunctionTesting]` | The message is built as a request and the pipeline runs: routing, binding, the filters and the handler. Names no cloud |
| Envelope | `[assembly: CloudRunTesting]` beside `[assembly: WebTesting]` | The push, CloudEvent or Scheduler request Cloud Run actually receives is built and posted to the test's host, so the front door, the adapter, the decoding and the metadata headers run too |
| Socket | `[KestrelRuntime]` on a class, under `[assembly: KestrelTesting]` | The same envelope over a loopback socket, on the Kestrel host the container runs |
| Container | A project in the simulator solution | The build output in `mcr.microsoft.com/dotnet/aspnet:8.0` with `PORT` set, driven by the Pub/Sub emulator or a hand-built request, observed through what the service prints |

The first three are in the fixture's test project and run with `dotnet test`. The fourth is a
project of its own that needs Docker, and it is in the repository rather than in a package; see
[the container tier](#the-container-tier).

```csharp
// Bootstrap.cs
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`[FunctionTesting]` makes the generated [trigger façades](/guide/triggers#testing) resolvable.
`[CloudRunTesting]` replaces the delivery behind them, and needs `[WebTesting]` beside it, because
that is what registers the host the envelope is posted to; the delivery says so if it is missing.
`[KestrelTesting]` makes `[KestrelRuntime]` on a class mean "on a socket".

**No test method changes when a line is added or removed.** Delete `[CloudRunTesting]` to drop back
to the pipeline, or keep it and mark one class `[PipelineDelivery]` to run that class through the
pipeline alone; `[PipelineDelivery]` is in `Hardened.Functions.Testing`, the counterpart of
`[PipelineHost]`.

Neither attribute cares which order it is applied in. The neutral delivery registers with `TryAdd`
and the Cloud Run one replaces it, so both orders end with the envelope delivery.

## The façades

One nested class per trigger kind on the entry point, with a method per source:

| Trigger | Façade | A method named for the |
|---|---|---|
| `[Queue]` | `Application.Queues` | subscription |
| `[Topic]` | `Application.Topics` | topic |
| `[Timer]` | `Application.Timers` | schedule |
| `[Change]` | `Application.Changes` | collection |
| `[Blob]` | `Application.Blobs` | bucket |
| `[HardenedFunction]` | `Application.Invocations` | operation |

`[Event]` has no façade, because it is addressed by source and type where every other trigger has
one name. A test sends through `ITriggerDelivery` itself:

```csharp
[HardenedTest]
public async Task AnEventReachesItsHandler(ITriggerDelivery delivery, [Mock] IFulfilment fulfilment) {
    await delivery.Deliver([new Order { Id = "A-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

    fulfilment.Received().Start(Arg.Is<Order>(order => order.Id == "A-1"));
}
```

A method exists because the handler does, and its parameter is the type the handler binds. A
renamed source or a changed payload is a compile error in the test rather than a test that quietly
passes against nothing. `[Queue("orders-new")]` becomes `queues.OrdersNew(...)`, and passing
several payloads sends one delivery per payload.

An invocation returns, so its façade method returns the handler's declared type.

## What each envelope is

Under `[CloudRunTesting]` the delivery builds the wire shape for the scheme the handler declared:

| Scheme | The request |
|---|---|
| `QUEUE` | A Pub/Sub push: the payload base64 in `message.data`, a message id, a publish time and the subscription `projects/test-project/subscriptions/{queue}` |
| `TOPIC` | A binary-mode CloudEvent typed `google.cloud.pubsub.topic.v1.messagePublished`, with the push body as its data and the topic in `ce-source` |
| `TIMER` | A POST to `/_triggers/timer/{name}` with `X-CloudScheduler`, `X-CloudScheduler-JobName` and `X-CloudScheduler-ScheduleTime` |
| `INVOKE` | A POST to `/_triggers/invoke/{operation}`, with the answer read back from the response |
| `BLOB` | A `google.cloud.storage.object.v1.finalized` CloudEvent, with the payload's `name` and `size` in the object metadata |
| `CHANGE` | A Firestore `DocumentEventData`, `application/protobuf`, with the payload written as the document's fields, as the value and the old value both |
| `EVENT` | A binary-mode CloudEvent whose source and type come from the route |

A response Pub/Sub would acknowledge, 102 or any 2xx, is a success. Anything else is thrown to the
test as the handler's own exception when there was one, so a test asserts the failure it expects.

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

## Web handlers

A Cloud Run web application's routes are ordinary routes, so
[`ITestWebApp`](/guide/testing-web) drives them with no Cloud Run involvement, and
`[KestrelRuntime]` on a class runs them on a socket. `[CloudRunTesting]` changes nothing about a
web route; a test project with no triggers does not need it.

A test can also post a trigger's wire shape by hand through `ITestWebApp`, with the `ce-*` or
`x-goog-pubsub-*` headers on the request, when the thing under test is the envelope itself.

## The container tier

The last rung runs the fixture the way Cloud Run runs it: the build output mounted into
`mcr.microsoft.com/dotnet/aspnet:8.0`, started as `dotnet <assembly>.dll` with `PORT` set, on a
Testcontainers network. A queue is driven by the Pub/Sub emulator holding a push subscription
whose endpoint is the container, published to through Google's own client with
`PUBSUB_EMULATOR_HOST` set. A schedule, an invocation and an event are driven by the request
Scheduler, a caller or Eventarc would send, built by hand because none of those has an emulator. A
blob is driven by the notification form through the emulator, because that is the form an emulator
can deliver.

What the tier proves is the container contract: the process comes up on `PORT`, a message
published to the emulator reaches the handler, a handler that throws sees the same message again,
and `SIGTERM` lets a request in flight finish. The test observes the service through lines it
prints, because a `[Mock]` cannot reach into another process.

These projects live in `Hardened.Simulators.slnx` in the repository, not in a package, and they
need a Docker daemon. On a machine without one they fail at container startup rather than
skipping. The emulator image is pinned, because the tag `Testcontainers.PubSub` defaults to is no
longer served.

## Next

- [Triggers](/guide/triggers): the façades, and what each source delivers
- [Writing a test](/guide/testing): the harness this builds on
- [Test hosts](/guide/testing-hosts): the seam `[KestrelRuntime]` on a class plugs into
