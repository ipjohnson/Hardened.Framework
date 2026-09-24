# Testing functions

`[FunctionTesting]` makes the trigger façades test parameters. A test takes the façade for a trigger
kind, such as `Application.Queues`, and calls the method named for a source.

The `hardened-function` template writes this handler in `src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The first test in `tests/Orders.Tests/OrderHandlerTests.cs` sends it one message:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

A façade call delivers its messages to the handler through the application's pipeline. Routing,
binding, the filters and the handler run, with no cloud. The build generates the façades from the
handlers' trigger attributes.

`OrderLog` is the template's `[SingletonService]` class that keeps the orders it is given. The test
sees what the handler recorded, because a test parameter is one object for the whole test.
[Writing a test](/guide/testing) covers parameters.

## Setting up the test project

`[FunctionTesting]` is in the package `Hardened.Functions.Testing`, in the namespace of the same
name. The template writes this line in `tests/Orders.Tests/Orders.Tests.csproj`:

```xml
<PackageReference Include="Hardened.Functions.Testing" />
```

Its version is in `Directory.Packages.props`. `tests/Orders.Tests/Bootstrap.cs` declares the
attributes on the test assembly:

```csharp
using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Orders;

[assembly: HardenedTestEntryPoint(typeof(Application))]
[assembly: FunctionTesting]
[assembly: NSubstituteSupport]
```

| Attribute | Role |
|---|---|
| `[HardenedTestEntryPoint]` | Names the application whose façades the test takes |
| `[FunctionTesting]` | Makes the façades test parameters |
| `[NSubstituteSupport]` | The mock library attribute. Only [`[Mock]`](#substituting-a-service) needs it |

`[FunctionTesting]` goes on the assembly, a class or a method. Without it, a façade parameter fails
the test with this message:

```text
Unable to resolve service for type 'Hardened.Requests.Abstract.Execution.TriggerSend' while attempting to activate 'Orders.Application+Queues'.
```

The `hardened-function` template writes this project, with a cloud testing attribute beside
`[FunctionTesting]`. [Delivering through a cloud's envelope](#delivering-through-a-cloud-s-envelope)
covers that attribute. [Writing a test](/guide/testing) covers the entry point, the runner packages
and the rest of the project.

## Trigger façades

Each trigger kind the application handles gets a façade, a class nested in the application class. A
kind with no handler gets none. A façade has one method per source.

| Trigger | Façade | Example source | Method |
|---|---|---|---|
| `[Queue]` | `Application.Queues` | `[Queue("orders-new")]` | `OrdersNew(params Order[] messages)` |
| `[Topic]` | `Application.Topics` | `[Topic("order-events")]` | `OrderEvents(params Order[] messages)` |
| `[Timer]` | `Application.Timers` | `[Timer("nightly-rollup")]` | `NightlyRollup()` |
| `[Change]` | `Application.Changes` | `[Change("orders")]` | `Orders(params Order[] messages)` |
| `[Stream]` | `Application.Streams` | `[Stream("order-stream")]` | `OrderStream(params Order[] messages)` |
| `[Blob]` | `Application.Blobs` | `[Blob("uploads")]` | `Uploads(params Upload[] messages)` |
| `[HardenedFunction]` | `Application.Invocations` | `[HardenedFunction]` on `Process` | `Process(Order message)`, returning the handler's type |
| `[Event]` | None | | |

`[Event]` has no façade. A test sends an event through
[`ITriggerDelivery`](#sending-through-itriggerdelivery).

A façade method takes `params` of the type the handler binds, so one message and several are the
same call. The call writes each message as JSON with camelCase property names before delivering
it. The second test in `OrderHandlerTests` sends three messages:

```csharp
    [HardenedTest]
    public async Task EveryMessageInABatchIsHandled(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(
            new Order { Id = "A-1" },
            new Order { Id = "A-2" },
            new Order { Id = "A-3" }
        );

        Assert.Equal(3, log.Orders.Count);
    }
```

A handler that takes no payload gets a method with no parameters. For `--trigger timer`, the
template's handler is `[Timer("nightly")] public void OnNightly() => log.Sweep();`. The template's
test calls `timers.Nightly()`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task TheScheduleReachesTheHandler(Application.Timers timers, OrderLog log)
    {
        await timers.Nightly();

        Assert.Equal(1, log.Sweeps);
    }
}
```

The build writes the façades to `Application.Triggers.cs`, under
`obj/Debug/net8.0/generated/Hardened.Function.SourceGenerator/`.

### Method names

The build names each method after the source's name in the trigger attribute. It drops every
character that is not a letter or a digit, and starts a new word there. It makes the first letter of
each word upper case. The other letters keep their case. A name that starts with a digit gets a
leading underscore.

| Source name | Method |
|---|---|
| `orders` | `Orders` |
| `orders-new` | `OrdersNew` |
| `orders.v2` | `OrdersV2` |
| `ordersV3` | `OrdersV3` |
| `2024-archive` | `_2024Archive` |

Renaming a source renames its method. Changing the handler's payload type changes the method's
parameter. A test written against the old method then stops compiling.

Two sources of one kind whose names give the same method name raise the build warning `HRDF002`.
The façade reaches only the first. Both sources still route to their handlers. From
`[Queue("orders-new")]` and `[Queue("orders_new")]`, the warning reads:

```text
The queues 'orders_new' and 'orders-new' both produce the test method 'OrdersNew', so only 'orders-new' can be reached through a trigger façade. Rename one of them, or suppress HRDF002 to keep the collision.
```

A queue and a topic with the same name are on two façades, so their methods do not collide.

## Invocations

`Application.Invocations` has a method for each `[HardenedFunction]` handler. The method takes one
message and returns what the handler returned. For `--trigger invoke`, the template writes this
handler in `src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Requests.Abstract.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [HardenedFunction]
    public OrderAccepted Process(Order order)
    {
        log.Record(order);

        return new OrderAccepted(order.Id, log.Orders.Count);
    }
}

public record OrderAccepted(string Id, int Received);
```

Its test in `tests/Orders.Tests/OrderHandlerTests.cs` takes the façade:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task ThePayloadReachesTheHandler(Application.Invocations invocations, OrderLog log)
    {
        var accepted = await invocations.Process(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", accepted.Id);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The method's name comes from the function's name, by the rule in [Method names](#method-names). A
handler with no name is named after its method. A `void` handler's method returns `Task`.

Under `[FunctionTesting]` alone, the call returns the object the handler returned. Under a cloud
testing attribute, the call returns the handler's JSON answer, read back into the declared type.

A `[HardenedFunction]` with no name answers every invocation that no named handler matches. With a
name on each handler, each call runs its own handler.

::: warning
Two `[HardenedFunction]` handlers without names in one application send every invocation to one of
them. The façade still offers a method for each. A test that calls the other one's façade method
gets the first handler's answer and no error. Give each handler a name.
:::

[Triggers](/guide/triggers) covers `[HardenedFunction]`. Google Cloud's [Invocations](/gcp/invoke)
page covers it on Cloud Run.

## Sending through `ITriggerDelivery`

`ITriggerDelivery` is what every façade sends through. A test can take it as a parameter.

| Method | Does |
|---|---|
| `Deliver(messages, scheme, path)` | Delivers a batch to the handler registered for the scheme and the path |
| `Call(message, scheme, path, responseType)` | Makes one invocation and returns its answer |

The scheme is the trigger kind's name in capitals:

| Trigger | Scheme |
|---|---|
| `[Queue]` | `QUEUE` |
| `[Topic]` | `TOPIC` |
| `[Timer]` | `TIMER` |
| `[Change]` | `CHANGE` |
| `[Stream]` | `STREAM` |
| `[Blob]` | `BLOB` |
| `[HardenedFunction]` | `INVOKE` |
| `[Event]` | `EVENT` |

The path is `/` followed by the source's name. The path of an `[Event]` handler is
`/{source}/{detailType}`. A test sends an event with `Deliver`. [Triggers](/guide/triggers) covers
declaring an `[Event]` handler, such as `PlacedHandler` in `src/Orders/PlacedHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class PlacedHandler(OrderLog log)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnPlaced(Order order) => log.Record(order);
}
```

`tests/Orders.Tests/OrderEventTests.cs` sends it an event:

```csharp
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderEventTests
{
    [HardenedTest]
    public async Task AnEventReachesItsHandler(ITriggerDelivery delivery, OrderLog log)
    {
        await delivery.Deliver([new Order { Id = "A-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

`Deliver` also reaches a source that `HRDF002` left without a method. A delivery to a scheme and
path that no handler serves fails with `InvalidOperationException`:

```text
No handler is registered for QUEUE /no-such-queue. An event source is wired to this function that no trigger attribute declared.
```

## State between calls

Under `[FunctionTesting]` alone, each façade call to a trigger builds a container of its own. The
messages of one call share one container. A test parameter is the same object in every one of those
containers, so the test sees what each call recorded. `[Shared]` on a façade parameter changes
nothing. [Writing a test](/guide/testing) covers which objects every container shares.

Each delivery keeps state differently. This table is for a `[SingletonService]` that the handler
uses and the test does not take:

| Delivery | Messages in one call | Two trigger calls in one test | Two invocations in one test |
|---|---|---|---|
| `[FunctionTesting]` alone | One container | Two containers | One container, the test's own |
| `[LambdaTesting]` | One container | Two containers | Two containers |
| `[CloudRunTesting]` | A container per message | Two containers | Two containers |
| `[AzureFunctionsTesting]` | One container | One container, the test's own | Not built on Azure |

## Delivering through a cloud's envelope

A cloud testing attribute beside `[FunctionTesting]` changes how a façade call reaches the handler.
The call builds each message into what that cloud sends. The message goes in through the cloud's
adapter.

| Attribute | Package | Also needs | `[Event]` through `ITriggerDelivery` |
|---|---|---|---|
| `[LambdaTesting]` | `Hardened.Aws.Lambda.Testing` | Nothing | Throws `NotSupportedException` |
| `[CloudRunTesting]` | `Hardened.Gcp.CloudRun.Testing` | `[assembly: WebTesting]`, from `Hardened.Web.Testing` | Delivered |
| `[AzureFunctionsTesting]` | `Hardened.Azure.Functions.Testing` | Nothing | Delivered |

Each cloud attribute still needs `[FunctionTesting]` beside it for the façades. The order of the two
attributes does not matter. `[FunctionTesting]` registers its delivery only when none is
registered. A cloud attribute removes any delivery and registers its own. Each cloud attribute goes
on the assembly, a class or a method.

The template's `tests/Orders.Tests/Bootstrap.cs` for AWS declares both attributes:

```csharp
using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Orders;

[assembly: HardenedTestEntryPoint(typeof(Application))]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: NSubstituteSupport]
```

Adding or removing a cloud attribute changes no test method. Some results differ by attribute. An
`[Event]` fails under `[LambdaTesting]`, below. A failed message is handled differently under each
attribute, as [Batch failures](#batch-failures) shows.

Under `[LambdaTesting]`, sending an `[Event]` fails with `NotSupportedException`:

```text
No test envelope is built for the EVENT scheme yet. Queues, topics, timers, changes, streams and blobs have one; events are addressed by source and detail type and need their own shape.
```

`[PipelineDelivery]` on a class or a method puts its tests back on the delivery that
`[FunctionTesting]` alone uses. It is in `Hardened.Functions.Testing`. The event test from
[Sending through `ITriggerDelivery`](#sending-through-itriggerdelivery) passes there with the
attribute on its class:

```csharp
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

[PipelineDelivery]
public class OrderEventTests
{
    [HardenedTest]
    public async Task AnEventReachesItsHandler(ITriggerDelivery delivery, OrderLog log)
    {
        await delivery.Deliver([new Order { Id = "A-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

`[CloudRunTesting]` posts each message to the web test host, so it needs `[assembly: WebTesting]`
from `Hardened.Web.Testing` beside it. Without it, every call fails with this message:

```text
[CloudRunTesting] delivers through the web test host, and none is registered. Add [assembly: WebTesting] beside it; [KestrelRuntime] on a class under [assembly: KestrelTesting] then runs that class over a socket.
```

The testing page of each cloud covers what its delivery builds: [Testing](/aws/testing) on AWS,
[Testing](/gcp/testing) on Google Cloud and [Testing](/azure/testing) on Azure.

## Substituting a service

`[Mock]` on a parameter replaces the handler's service with a test double, as it does in any test.
The double is the same object in every container the test builds.
[Substituting services](/guide/testing-mocks) covers `[Mock]` and the mock libraries.

`src/Orders/OrderStore.cs` declares a service:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Orders;

public interface IOrderStore
{
    void Place(Order order);
}

[SingletonService]
public class OrderStore : IOrderStore
{
    private readonly List<Order> _placed = [];

    public void Place(Order order) => _placed.Add(order);
}
```

`src/Orders/PlacementHandler.cs` uses it:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class PlacementHandler(IOrderStore store)
{
    [Queue("orders-new")]
    public void OnNewOrder(Order order) => store.Place(order);
}
```

The first test in `tests/Orders.Tests/PlacementTests.cs` takes the store as a `[Mock]`:

```csharp
using DependencyModules.Testing.Attributes;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Orders.Tests;

public class PlacementTests
{
    [HardenedTest]
    public async Task AnOrderIsPlaced(Application.Queues queues, [Mock] IOrderStore store)
    {
        await queues.OrdersNew(new Order { Id = "A-1" });

        store.Received().Place(Arg.Is<Order>(order => order.Id == "A-1"));
    }
}
```

## Batch failures

A call with several messages is one batch. The handler runs once per message. By default, a failed
message fails the whole call. The call throws the handler's own exception. The messages after the
failed one do not run. The second test in `PlacementTests` makes the store throw on the second of
three orders:

```csharp
    [HardenedTest]
    public async Task AFailedMessageFailsTheCall(Application.Queues queues, [Mock] IOrderStore store)
    {
        store
            .When(one => one.Place(Arg.Is<Order>(order => order.Id == "A-2")))
            .Do(_ => throw new InvalidOperationException("refused A-2"));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queues.OrdersNew(
                new Order { Id = "A-1" },
                new Order { Id = "A-2" },
                new Order { Id = "A-3" }
            )
        );

        Assert.Equal("refused A-2", failure.Message);
        store.Received(2).Place(Arg.Any<Order>());
    }
```

`[FunctionTesting]` alone never reports failures per message, so it always behaves this way.
`[CloudRunTesting]` sends each message as a request of its own. Every message runs, and then the
call throws the first failure. The test above fails there on `Received(2)`, with three calls.

When the application reports failures per message, a failed message does not fail the call. The
source's `BatchFailureMode` then decides which messages run after it. The enum is in
`Hardened.Requests.Abstract.Execution`. [Triggers](/guide/triggers) covers the modes and the module
setting that turns reporting on.

The table is for a batch of three with reporting on, after the second message fails:

| `BatchFailureMode` | Messages that run | The call |
|---|---|---|
| `PerItem`, the mode of a queue | All three | Returns |
| `Checkpoint`, the mode of a change feed and of a stream | The first two | Returns |

The call does not return the failure report. A test asserts on what the handler did.
`tests/Orders.Tests/ReportingTests.cs` asserts that every message runs:

```csharp
using DependencyModules.Testing.Attributes;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Orders.Tests;

public class ReportingTests
{
    [HardenedTest]
    public async Task EveryMessageRunsAfterOneFails(Application.Queues queues, [Mock] IOrderStore store)
    {
        store
            .When(one => one.Place(Arg.Is<Order>(order => order.Id == "A-2")))
            .Do(_ => throw new InvalidOperationException("refused A-2"));

        await queues.OrdersNew(
            new Order { Id = "A-1" },
            new Order { Id = "A-2" },
            new Order { Id = "A-3" }
        );

        store.Received(3).Place(Arg.Any<Order>());
    }
}
```

The test passes under `[LambdaTesting]` in an application that reports failures per message, such as
one with `[SqsModule(ReportBatchItemFailures = true)]`. Under
`[FunctionTesting]` alone, it fails, because the call throws.

## Limits

The function testing attributes deliver to trigger handlers and `[HardenedFunction]` handlers only.
`ITriggerDelivery` has no way to send an HTTP request. A test calls a route, streamed or not,
through `ITestWebApp`. [Sending requests](/guide/testing-web) and [Test hosts](/guide/testing-hosts)
cover route tests.

Under `[FunctionTesting]` alone, a call that runs one handler with no batch does not report the
handler's exception. A `[Timer]` handler that throws leaves its call returning normally. A
`[HardenedFunction]` that throws makes the call throw `InvalidCastException`. Under `[LambdaTesting]`
the call throws the handler's own exception.

In a function whose only adapter module is `EventBridgeModule`, `ITriggerDelivery` under
`[FunctionTesting]` alone never runs an `[Event]` handler that takes a payload, and the call returns
normally. The event test above runs because the template's function also has `SqsModule`.

A test cannot see a response-cache hit through `Application.Invocations`. Under `[LambdaTesting]`,
each call has its own container and its own cache store. A second identical call runs the handler
again, with or without `[Shared]` on the façade parameter. Under `[FunctionTesting]` alone, the
second call is a hit. The façade then returns null.

A `[HardenedFunction]` handler can be called through `Application.Invocations` only when it returns
nothing, or a type that is neither generic nor an array. The façade method writes the return type
without its type arguments or array brackets:

| The handler returns | The façade method returns | What happens |
|---|---|---|
| `Task<Quote>` | `Task<Task>` | The call throws `InvalidCastException` under `[FunctionTesting]` alone, and `NotSupportedException` under `[LambdaTesting]` |
| `OrderAccepted[]` | `Task<OrderAccepted>` | The call throws `InvalidCastException` under `[FunctionTesting]` alone, and `JsonException` under `[LambdaTesting]` |
| `List<OrderAccepted>` | `Task<List>` | The application does not build: `CS0305`, `Using the generic type 'List<T>' requires 1 type arguments`, in `Application.Triggers.cs` |

## Next

- [Triggers](/guide/triggers): the trigger attributes, and `BatchFailureMode`
- [Writing a test](/guide/testing): the test project, parameters and the objects every container
  shares
- [Testing](/aws/testing) on AWS: what `[LambdaTesting]` builds
- [Testing](/gcp/testing) on Google Cloud: what `[CloudRunTesting]` builds
- [Testing](/azure/testing) on Azure: what `[AzureFunctionsTesting]` builds
