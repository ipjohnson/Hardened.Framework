# Testing

`[AzureFunctionsTesting]` builds each façade call into the trigger data that the isolated worker
binds for the generated function, such as the Service Bus messages of a queue, and invokes the
app's `FunctionsInvocationHandler` with it. The trigger's adapter reads the data before the
handler runs, as in a deployed function app.

For `--host azure --trigger queue`, the `hardened-function` template writes this handler in
`src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

For `--host azure`, the template writes this `tests/Orders.Tests/Bootstrap.cs`:

```csharp
using DependencyModules.NSubstitute;
using Hardened.Azure.Functions.Testing;
using Hardened.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Orders;

[assembly: HardenedTestEntryPoint(typeof(Application))]
[assembly: FunctionTesting]
[assembly: AzureFunctionsTesting]
[assembly: NSubstituteSupport]
```

The template's first test is in `tests/Orders.Tests/OrderHandlerTests.cs`:

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

No Functions host runs in the test. The worker's converters turn what the host sends into the
trigger data. `[AzureFunctionsTesting]` builds the trigger data directly, so the converters do not
run. The [container tests in the repository](#container-tests-in-the-repository) run the host.

## Setting up a test project

`[AzureFunctionsTesting]` is in the `Hardened.Azure.Functions.Testing` package, in the namespace of
the same name. It goes on the assembly, a class or a method, beside `[FunctionTesting]`. The package
references every Azure adapter package. `dotnet new hardened-function --host azure` writes the
package reference in the test project, and the `Bootstrap.cs` above.

[Testing functions](/guide/testing-functions) covers `[FunctionTesting]`, the façades,
`ITriggerDelivery`, `[PipelineDelivery]`, how the two attributes combine, and what a failed message
does to a call. Under `[AzureFunctionsTesting]`, an `ITriggerDelivery` parameter is
`FunctionsTriggerDelivery`.

## The trigger data for each trigger

Under `[AzureFunctionsTesting]`, a façade call hands the function this trigger data:

| Trigger | Invocations | The trigger data | Values in it |
|---|---|---|---|
| `[Queue]`, `[Topic]` | One per call | A `ServiceBusDelivery` of `ServiceBusReceivedMessage`s, one per message, built with `ServiceBusModelFactory` | Message ids `<source>-0`, `<source>-1` and so on, delivery count 1, content type `application/json`, sequence numbers from 1, and no application properties. The settlement actions come from the test's container. See [Settling Service Bus messages](#settling-service-bus-messages) |
| `[Timer]` | One per message, or one for a call with none | The timer's state as JSON | `Last` and `LastUpdated` an hour before the call, `Next` an hour after it, `IsPastDue` false. The messages themselves are not delivered |
| `[Change]` | One per call | A JSON array with a document per message | Each document is the message with `_rid` `rid-0`, `_etag` `"00000000"`, `_ts` the current time and `_lsn` from 100, in order. A message with no `id` property gets `document-0` and so on |
| `[Stream]` | One per call | An `EventData[]` built with `EventHubsModelFactory`, one per message | Partition key the hub's name, message ids `<hub>-0` and so on, sequence numbers from 1, offsets 64, 128 and so on, content type `application/json` |
| `[Blob]` | One per message | A `BlobClient` for the blob, and the blob's properties beside it | The container the source names, the message's `name` or `key` or else `blob-0` and so on, the message's `size` or else 0, content type `application/octet-stream`. [Blobs](/azure/blob) lists the rest |
| `[Event]`, sent through `ITriggerDelivery` | One per message, to the function `Event` | A structured CloudEvent as JSON | `specversion` `1.0`, a new GUID as `id`, `source` and `type` from the path, the current time, `datacontenttype` `application/json`, and the message as `data` |
| `[HardenedFunction]` | None | None | An Azure app with one fails to build with `HRDF001`. See [Triggers](/guide/triggers) |

The delivery writes every message as JSON with camelCase property names.
[Testing functions](/guide/testing-functions) covers the serialization.

[Queues](/azure/queue), [Topics](/azure/topic), [Timers](/azure/timer), [Events](/azure/event),
[Changes](/azure/change), [Streams](/azure/stream) and [Blobs](/azure/blob) list the headers that
each adapter gives a handler, and what else the trigger data holds. Under `[FunctionTesting]` alone,
a trigger handler's request carries none of these headers. The body is the test's message, so a
blob's `bucket` is empty unless the message sets it.

The delivery drops the leading `/` of an event's path. An Azure resource ID starts with `/`, so the
delivery does not reach an `[Event]` whose source is a resource ID. [Events](/azure/event) shows the
failure.

A handler's `CancellationToken` is never cancelled, under `[AzureFunctionsTesting]` or under
`[FunctionTesting]` alone.

## Settling Service Bus messages

With `[ServiceBusModule(ReportsItemFailures = true)]` on the application, the function completes and
abandons each message itself, through the `ServiceBusMessageActions` that the worker binds.
[Queues](/azure/queue) covers the setting. Under `[AzureFunctionsTesting]`, the delivery takes the
actions from the test's container.

::: warning
Under `[AzureFunctionsTesting]`, `[ServiceBusModule(ReportsItemFailures = true)]` does nothing
unless the test's container holds a `ServiceBusMessageActions`. Without one, a failed message fails
the call with the handler's exception. The messages after it do not run. The deployed function does
not do this, so a test of failed messages passes against the wrong behaviour.
:::

`RecordingMessageActions`, in `Hardened.Azure.Functions.Testing`, is a `ServiceBusMessageActions`
that records the id of each message it settles. `ServiceBusMessageActions` is in the
`Microsoft.Azure.Functions.Worker` namespace, from the worker's Service Bus extension. The testing
package brings that extension.

| Member | Holds |
|---|---|
| `Completed` | The ids of the messages completed, in order |
| `Abandoned` | The ids of the messages abandoned, in order |
| `DeadLettered` | The ids of the messages dead-lettered. The Service Bus adapter never dead-letters |
| `Deferred` | The ids of the messages deferred. The Service Bus adapter never defers |

This application turns `ReportsItemFailures` on:

```csharp
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[ServiceBusModule(ReportsItemFailures = true)]
public partial class Application;
```

The handler refuses an order with a negative quantity:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order)
    {
        if (order.Quantity < 0)
        {
            throw new InvalidOperationException($"refused {order.Id}");
        }

        log.Record(order);
    }
}
```

The `[TestExport]` on this test class registers a `RecordingMessageActions` for its tests. A
`ServiceBusMessageActions` parameter is then that object.
[Substituting services](/guide/testing-mocks) covers `[TestExport]`.

```csharp
using DependencyModules.Testing.Attributes;
using Hardened.Azure.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orders.Tests;

[TestExport(
    typeof(ServiceBusMessageActions),
    Implementation = typeof(RecordingMessageActions),
    Lifetime = ServiceLifetime.Singleton
)]
public class SettlementTests
{
    [HardenedTest]
    public async Task OnlyTheRefusedMessageIsAbandoned(
        Application.Queues queues,
        ServiceBusMessageActions actions,
        OrderLog log
    )
    {
        await queues.Orders(
            new Order { Id = "A-1", Quantity = 2 },
            new Order { Id = "A-2", Quantity = -1 },
            new Order { Id = "A-3", Quantity = 1 }
        );

        var settled = Assert.IsType<RecordingMessageActions>(actions);

        Assert.Equal(["orders-1"], settled.Abandoned);
        Assert.Equal(["orders-0", "orders-2"], settled.Completed);
        Assert.Equal(2, log.Orders.Count);
    }
}
```

Every message of the call runs. The adapter abandons the refused message and completes the others.
The call returns. The ids are the ones the delivery gives, from the queue's name and the message's
position. [The trigger data for each trigger](#the-trigger-data-for-each-trigger) lists them.

Without `Lifetime = ServiceLifetime.Singleton`, the export is transient. The delivery then settles
through one instance. The test's parameter is another instance, and it records nothing.

Under `[FunctionTesting]` alone, the call throws at the refused message and settles nothing.

## Invoking a function by hand

For a message that the façades do not build, such as one with application properties, its own id
or a delivery count above 1, a test can build the trigger data itself and invoke the function
through `FunctionsInvocationHandler`.

| Type | Namespace | Role |
|---|---|---|
| `FunctionsInvocationHandler` | `Hardened.Azure.Functions.Runtime.Hosting` | A test parameter, like any service of the application. `Invoke(FunctionsTrigger trigger, FunctionContext functionContext)` runs one invocation |
| `FunctionsTrigger` | `Hardened.Azure.Functions.Runtime.Execution` | Carries the scheme, the route and the trigger data |
| `ServiceBusDelivery` | `Hardened.Azure.Functions.ServiceBus` | The trigger data of a queue or a topic: the messages, and the actions that settle them |
| `TestFunctionContext` | `Hardened.Azure.Functions.Testing` | A `FunctionContext` built from a function name, the binding data the host would send, and the services to resolve from |
| `ServiceBusModelFactory` | `Azure.Messaging.ServiceBus` | Its `ServiceBusReceivedMessage` method builds a received message |

The other triggers take the types listed under
[The trigger data for each trigger](#the-trigger-data-for-each-trigger).

This test uses the application and the handler from
[Settling Service Bus messages](#settling-service-bus-messages):

```csharp
using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class ByHandTests
{
    [HardenedTest]
    public async Task AMessageBuiltByHandIsSettled(
        FunctionsInvocationHandler handler,
        IServiceProvider services,
        OrderLog log
    )
    {
        var messages = new[]
        {
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString("""{"id":"A-1","quantity":2}"""),
                messageId: "m-1",
                contentType: "application/json",
                deliveryCount: 5,
                properties: new Dictionary<string, object> { ["tenant"] = "acme" }
            ),
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString("""{"id":"A-2","quantity":-1}"""),
                messageId: "m-2",
                contentType: "application/json"
            ),
        };

        var actions = new RecordingMessageActions();

        await handler.Invoke(
            new FunctionsTrigger("QUEUE", "/orders", new ServiceBusDelivery(messages, actions)),
            new TestFunctionContext("Queue_orders", new Dictionary<string, object?>(), services)
        );

        Assert.Equal(["m-1"], actions.Completed);
        Assert.Equal(["m-2"], actions.Abandoned);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The scheme and the route are the handler's, `QUEUE` and `/orders`. The function name is the one
the build writes, `Queue_orders`. The Azure [Overview](/azure/) covers the function names.

The message's id, its delivery count and each application property reach the handler as headers.
[Queues](/azure/queue) covers the headers.

| In the message | Header |
|---|---|
| The id | `x-azure-servicebus-message-id` |
| The delivery count | `x-azure-servicebus-delivery-count` |
| Each application property | One header per property |

A `ServiceBusDelivery` built without actions settles nothing. A failed message then fails the
invocation with the handler's exception.

The test passes under `[AzureFunctionsTesting]` and under `[FunctionTesting]` alone, because it does
not go through the delivery.

## Testing a web application

`[AzureFunctionsWebTesting]` builds each request that a web test makes into the worker's
`HttpRequestData`, and invokes the app's `FunctionsInvocationHandler` with it. It reads the
`HttpResponseData` that comes back as the test's response. No Functions host runs.

`[AzureFunctionsWebTesting]` is in `Hardened.Azure.Functions.Testing`, the same package as
`[AzureFunctionsTesting]`. It goes on a method, a class or the assembly. For
`--host azure-functions`, the `hardened-web` template writes these lines in
`tests/Todos.Tests/Bootstrap.cs`:

```csharp
using Hardened.Azure.Functions.Testing;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: AzureFunctionsWebTesting]
```

Every test in that project runs through the worker's HTTP adapter.
[Test hosts](/guide/testing-hosts) covers the attribute beside the other test hosts.

The request's URL is `http://functions.test/api/` followed by the path and the query. The path also
goes in the binding data under `path`. The adapter reads it there. A handler sees the path that the
test sent, whatever route prefix the host project's `host.json` sets.

| Case | What the test sees |
|---|---|
| The server address and scheme a handler reads | `functions.test` and `http`, with no port |
| The client address a handler reads | The first address of the test's `X-Forwarded-For` header, else none |
| A `Cookie` header set in the test | The request's cookies, so `[FromCookie]` binds |
| A cookie the handler sets | A `Set-Cookie` header on the response |
| A binary body | The bytes the handler wrote |
| A response compressed by `[Compress]` | `Content-Encoding: gzip`, and `Deserialize<T>()` reads it |
| A typed client over `HttpClient` | Reads the body, because `Content-Type` goes on the response's content |

Every request of a test runs in the test's own container, so what one request stores, the next one
reads:

```csharp
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class TodoFunctionTests
{
    private record NewTodoRequest(string Title);

    private record TodoResponse(int Id, string Title, bool Done);

    [HardenedTest]
    public async Task EveryRequestOfATestSharesItsContainer(ITestWebApp app)
    {
        (await app.Post(new NewTodoRequest("Write a test"), "/todos")).Assert.Ok();

        var todos = (await app.Get("/todos")).Deserialize<List<TodoResponse>>();

        Assert.Equal([1, 2, 3], todos.Select(todo => todo.Id));
    }
}
```

The template's store starts with two todos, so the POST adds the third. The GET in the same test
lists it. On the pipeline host, each request has a container of its own, so the GET lists 1 and 2.
The template's `ContainerIsolationTests` carries `[PipelineHost]`, which keeps that class on the
pipeline host. [Writing a test](/guide/testing) covers containers and `[PipelineHost]`.

`[AzureFunctionsWebTesting]` runs the application's startup services before the first request, as
the worker does when it starts.

The attribute does not apply `[Grants]` or `[Subject]`. It records no `LastResponse`.
[Test hosts](/guide/testing-hosts) covers both.

## Checking the function metadata

The build describes the app's functions twice. Hardened's generator writes a metadata provider,
which the host indexes. The Worker SDK's build task writes a `functions.metadata` file from the same
functions. The Azure [Overview](/azure/) covers the provider and `functions.metadata`.

`MetadataAgreement.Disagreements(provider)` compares the two and returns what differs. It compares
the function names, then each function's `entryPoint`, `scriptFile`, `language` and bindings. It
compares the bindings as JSON, whatever the order of their keys. An empty list is agreement.

The provider's name is the application class's name followed by `AzureFunctionMetadataProvider`,
such as `ApplicationAzureFunctionMetadataProvider`. Its namespace is the application's namespace
followed by `.Generated`, such as `Orders.Generated`. This test checks the `Orders` app:

```csharp
using Hardened.Azure.Functions.Testing;
using Orders.Generated;
using Xunit;

namespace Orders.Tests;

public class MetadataAgreementTests
{
    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree()
    {
        Assert.Empty(
            await MetadataAgreement.Disagreements(new ApplicationAzureFunctionMetadataProvider())
        );
    }
}
```

The test is a plain xUnit `[Fact]`. It needs no container. Without a `directory` argument,
`Disagreements` reads `functions.metadata` from the test's output directory. The build copies the
referenced application's file there. With no file there, `Disagreements` returns one disagreement:
the text `The build task wrote no functions.metadata at`, followed by the path.

## Container tests in the repository

`Hardened.Simulators.slnx`, at the root of the repository, holds the tests that run the repository's
Azure fixtures in the Functions host. Its Azure folder has three test projects, under
`src/Clouds/Azure/IntegrationTests`.

| Project | Test class | The fixture | What the tests send and check |
|---|---|---|---|
| `Hardened.IntegrationTests.AzureQueue.Host.Tests` | `HostImageTests` | A queue handler | The host indexes `Queue_orders` and nothing else. A message published to the emulator reaches the handler. The host logs that the invocation succeeded. A message the handler refuses fails the invocation and is delivered again |
| `Hardened.IntegrationTests.AzureEvents.Host.Tests` | `HostImageTests` | A queue, a topic, a timer and an `[Event]` handler in one app | The host indexes `Event`, `Queue_orders_new`, `Timer_nightly_rollup` and `Topic_order_events`. A message published to the topic and one published to the queue each reach their own handler |
| `Hardened.IntegrationTests.AzureStream.Host.Tests` | `HostImageTests` | A stream handler | The host indexes `Stream_clickstream` and nothing else. Two events published in one batch reach the handler in order |

Each test mounts a fixture's build output at `/home/site/wwwroot` in the image
`mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0`. The Functions host in that
image indexes the functions that the generated provider declares. Azurite 3.35.0 is the storage
account that the host needs. The Service Bus emulator 2.0.1, with the SQL Server it keeps its state
in, or the Event Hubs emulator 2.2.1, is the event source. The containers share a Docker network.
The host reaches the emulators by network alias.

The tests publish with the Azure SDK's own clients, `ServiceBusClient` and `EventHubProducerClient`.
A `[Mock]` cannot reach into another process, so the tests read what a handler did from the lines
that the fixture prints with the prefix `HARDENED-OBSERVED `. They also read the host's own log:
the function list under `Found the following functions:`, and
`Executed 'Functions.<name>' (Succeeded` or `(Failed`.

`dotnet test` on one of the projects runs it. The tests need Docker. This command, from the root of
the repository, runs the stream project:

```bash
dotnet test src/Clouds/Azure/IntegrationTests/Stream/Hardened.IntegrationTests.AzureStream.Host.Tests
```

Without a Docker daemon, the tests fail instead of skipping. The host image is published for amd64
only, so an Apple Silicon machine runs it under emulation. The harness,
`Hardened.Functions.Testing.Containers`, is not a package.

No container test sends anything to a blob, change feed, timer, Event Grid or web function. The
events project checks that the host indexes its timer and `Event` functions. It sends them nothing.
The blob, change feed and web fixtures run in-process only.

## Next

| Page | Covers |
|---|---|
| [Testing functions](/guide/testing-functions) | The façades, `[FunctionTesting]`, `[PipelineDelivery]` and failed messages in a test |
| [Test hosts](/guide/testing-hosts) | Every test host, `[AzureFunctionsWebTesting]` among them |
| [Queues](/azure/queue) | `ReportsItemFailures`, and the headers a Service Bus message gives a handler |
| [Web applications](/azure/web) | What a deployed web function app does not run |
| [Overview](/azure/) | The functions the build writes, running a function app locally, and deploying it |
