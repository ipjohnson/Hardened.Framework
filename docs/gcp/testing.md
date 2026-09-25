# Testing

`[CloudRunTesting]` builds each message of a façade call into the request its Google Cloud source
sends, and posts it to the test's web host. The service's `TriggerFrontDoor` and the trigger's
adapter read the request before the handler runs, as they do in a deployed service.

The `hardened-function` template writes this handler in `src/Orders/OrderHandler.cs` for
`--trigger queue`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

For `--host gcp`, the template writes this `tests/Orders.Tests/Bootstrap.cs`:

```csharp
using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Orders;

[assembly: HardenedTestEntryPoint(typeof(Application))]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: NSubstituteSupport]
```

The first test in `tests/Orders.Tests/OrderHandlerTests.cs` sends one message:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

`queues.Orders` posts a Pub/Sub push for the subscription `orders`. The Pub/Sub adapter reads the
push before `OnOrder` runs.

## Turning it on

`[CloudRunTesting]` is in the package `Hardened.Gcp.CloudRun.Testing`, in the namespace of the same
name. It goes on the assembly, a class or a method. It goes beside `[FunctionTesting]`. It needs
`[assembly: WebTesting]`. [Testing functions](/guide/testing-functions) covers `[FunctionTesting]`,
the façades, `[PipelineDelivery]`, `[assembly: WebTesting]` and what a failed message does to a
call.

`dotnet new hardened-function --host gcp` references `Hardened.Gcp.CloudRun.Testing` and
`Hardened.Web.Testing` in the test project.

Under `[CloudRunTesting]`, an `ITriggerDelivery` parameter is `CloudRunEnvelopeDelivery`. An
`ITestHost` parameter is `PipelineHost`.

## The request each trigger gets

`[CloudRunTesting]` posts these requests:

| Trigger | `[CloudRunTesting]` posts | Values in the request |
|---|---|---|
| `[Queue]` | A Pub/Sub push to `/`, one per message | The subscription `projects/test-project/subscriptions/<queue>`, message ids `<queue>-0`, `<queue>-1` and so on. No attributes, ordering key or delivery attempt |
| `[Topic]` | A binary-mode CloudEvent of type `google.cloud.pubsub.topic.v1.messagePublished` to `/`, one per message, with a push as its data | `ce-source` `//pubsub.googleapis.com/projects/test-project/topics/<topic>`, `ce-id` `<topic>-0` and so on, and the subscription `projects/test-project/subscriptions/eventarc-<topic>` |
| `[Timer]` | A `POST` to `/_triggers/timer/<name>`, with no body | `X-CloudScheduler: true`, `X-CloudScheduler-JobName` set to the name, `X-CloudScheduler-ScheduleTime` and `User-Agent: Google-Cloud-Scheduler` |
| `[Change]` | A binary-mode CloudEvent of type `google.cloud.firestore.document.v1.updated` to `/`, one per message, with a Firestore `DocumentEventData` as `application/protobuf` | The message as the document and as the old document. The document id is the message's `id`, or `document-0` and so on, and `ce-source` is `//firestore.googleapis.com/projects/test-project/databases/(default)` |
| `[Blob]` | A binary-mode CloudEvent of type `google.cloud.storage.object.v1.finalized` to `/`, one per message | The bucket the source names, and the object the message's `name` or `key` names, or `object-0` and so on. [Blobs](/gcp/blob) lists the rest |
| `[HardenedFunction]` | A `POST` to `/_triggers/invoke/<name>` with the message as JSON | The call returns the response body, read back as the handler's return type |
| `[Event]`, sent through `ITriggerDelivery` | A binary-mode CloudEvent to `/`, one per message | `ce-source` and `ce-type` from the route, and `ce-id` `<type>-0` and so on |

Every request names the project `test-project`. Every time in a request is
`2026-01-01T00:00:00.000Z`.

[Queues](/gcp/queue), [Topics](/gcp/topic), [Timers](/gcp/timer), [Events](/gcp/event),
[Changes](/gcp/change), [Blobs](/gcp/blob) and [Invocations](/gcp/invoke) list the headers each
adapter gives a handler, and what else the request holds. Under `[FunctionTesting]` alone, a trigger
handler's request carries none of these headers. A blob's `Bucket` is then empty unless the test
sets it.

Under `[CloudRunTesting]`, `[OldValue]` binds the same document as the handler's parameter, because
the request carries the message as both. Under `[FunctionTesting]` alone, a handler that binds
`[OldValue]` fails with an `InvalidOperationException`:

```text
[OldValue] was bound on a handler that is not serving a Firestore change. It reads the document event off the request, so it only works under [Change].
```

A change handler that binds `[OldValue]` is tested under `[CloudRunTesting]`.

`[CloudRunTesting]` posts timers to `/_triggers/timer/` and invocations to `/_triggers/invoke/`
whatever `Prefix` the application's `[SchedulerModule]` or `[InvokeModule]` sets.
When the application sets another `Prefix`, the service answers 404 and the call fails.
[Timers](/gcp/timer) and [Invocations](/gcp/invoke) show the message.

A blob call always sends a finalize. [Posting a delivery by hand](#posting-a-delivery-by-hand) covers
sending another event.

## Requests and acknowledgement

Each message is a request of its own. On the pipeline host, the default, each request runs in a
container of its own. A socket host serves every request of a test from one container.
[Testing functions](/guide/testing-functions) and [Test hosts](/guide/testing-hosts) cover what
carries from one request to the next.

A façade call sends every message, then fails if any request was not acknowledged. A request is
acknowledged when it answers 102 or any 2xx status. A Google Cloud source reads these statuses as
success. On the pipeline host, the call throws the handler's own exception.

## Over a Kestrel socket

`[KestrelRuntime]` on a test class, with `[assembly: KestrelTesting]` in the test project, runs the
class on a Kestrel server. Kestrel is the host a Cloud Run service runs on. `[CloudRunTesting]` then
posts each message to that server over a loopback socket.

These lines add `Hardened.Web.Kestrel.Testing` to `Directory.Packages.props` and to
`tests/Orders.Tests/Orders.Tests.csproj`:

```xml
<PackageVersion Include="Hardened.Web.Kestrel.Testing" Version="$(HardenedVersion)" />
```

```xml
<PackageReference Include="Hardened.Web.Kestrel.Testing" />
```

These lines go in `tests/Orders.Tests/Bootstrap.cs`:

```csharp
using Hardened.Web.Kestrel.Testing;

[assembly: KestrelTesting]
```

`tests/Orders.Tests/OrderSocketTests.cs` sends the same message over a socket:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Xunit;

namespace Orders.Tests;

[KestrelRuntime]
public class OrderSocketTests
{
    [ModuleTest]
    public async Task AMessageReachesTheHandlerOverASocket(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

Over a socket, a failed message makes the call throw an `InvalidOperationException` with the status
and the response body, such as:

```text
The source would not read the answer to BLOB /fail as an acknowledgement: the service answered 500. It said: {"type":"ServerError","message":"The server could not complete this request.","details":""}
```

The handler's own exception does not reach the test.

A web route is tested with `ITestWebApp`, as on any host. `[CloudRunTesting]` changes only what a
façade call sends. [Test hosts](/guide/testing-hosts) covers `[KestrelRuntime]`, `[KestrelTesting]`
and the other hosts a web route can be tested on.

## Posting a delivery by hand

No façade sends a message with attributes or a delivery attempt, or a blob deleted rather than
written. A test can post the request the source sends through `ITestWebApp` instead. The request
goes through the same adapter as a delivery.

`PubSubPush.Body` in `Hardened.Gcp.CloudRun.Testing` writes a push body as Pub/Sub does. It takes the
subscription's full name and the data, which it writes base64-encoded. Attributes, a message id, a
publish time, an ordering key and a delivery attempt are optional.

This test in `tests/Orders.Tests/HandPostedPushTests.cs` posts a push with the attribute `tenant` and
a delivery attempt:

```csharp
using System.Text;
using DependencyModules.xUnit.Attributes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Web.Testing;
using Xunit;

namespace Orders.Tests;

public class HandPostedPushTests
{
    [ModuleTest]
    public async Task APushWithAnAttributeReachesTheHandler(ITestWebApp app, OrderLog log)
    {
        var push = PubSubPush.Body(
            "projects/my-project/subscriptions/orders",
            Encoding.UTF8.GetBytes("""{"id":"A-1","quantity":2}"""),
            new Dictionary<string, string> { ["tenant"] = "acme" },
            messageId: "2070443601311540",
            deliveryAttempt: 5
        );

        var response = await app.Post(
            push,
            "/",
            request => request.Headers["Content-Type"] = "application/json"
        );

        response.Assert.Ok();
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

[Queues](/gcp/queue) covers the headers a push gives the handler.

`app.Post` sends a `byte[]` body as itself. A push needs `Content-Type: application/json`.
`ITestWebApp` sends a body as `text/js` when the test sets no content type. The push then answers
404, because no adapter reads it.

`response.Assert.Ok()` fails unless the request reached a handler. It catches the 404 of a push that
no adapter reads, and the 500 of a delivery that no handler names.

A binary-mode CloudEvent is read whatever its content type. [Blobs](/gcp/blob) shows a test that
posts one.

A test that posts by hand takes `ITestWebApp`, so it needs `[assembly: WebTesting]`. It runs the same
under `[CloudRunTesting]` and under `[FunctionTesting]` alone. [Sending requests](/guide/testing-web)
covers `ITestWebApp`.

## Cloud Functions 2nd gen

A Cloud Functions 2nd gen build runs the same application class as the Cloud Run service. The tests
above run unchanged against it, because the test host runs the application class and not the
function's entry type. [Web services](/gcp/web) covers the entry type, `ApplicationCloudFunction`.

The repository's `Hardened.Gcp.Functions.Runtime.Tests` sends the same web request and the same
Pub/Sub push through `CloudFunctionHost` and through the pipeline host. It checks that both reach
the same handler with the same answer.

The test project of a Cloud Functions build reports `HRDGF002`. [Web services](/gcp/web) lists the
warning. `AutoGenerateEntryPoint` set to `false` in the test project removes it. The tests still
run. This line goes in the first `PropertyGroup` of `tests/Orders.Tests/Orders.Tests.csproj`:

```xml
<AutoGenerateEntryPoint>false</AutoGenerateEntryPoint>
```

### Testing the entry type

Google's package `Google.Cloud.Functions.Testing` runs a function's entry type in the test process.
`FunctionTestServer<TFunction>` starts the entry type through the Functions Framework, with the
startup its `[FunctionsStartup]` attribute names. `CreateClient()` returns an `HttpClient` that
sends to the function. The Functions Framework documentation covers the package in
[Testing Functions](https://github.com/GoogleCloudPlatform/functions-framework-dotnet/blob/main/docs/testing.md).

`Google.Cloud.Functions.Testing` 3.0.1 depends on `Google.Cloud.Functions.Hosting` 3.0.1, the version
[Hosts](/guide/hosts) shows. These lines add it to `Directory.Packages.props` and to
`tests/Orders.Tests/Orders.Tests.csproj`:

```xml
<PackageVersion Include="Google.Cloud.Functions.Testing" Version="3.0.1" />
```

```xml
<PackageReference Include="Google.Cloud.Functions.Testing" />
```

`tests/Orders.Tests/CloudFunctionTests.cs` posts a Pub/Sub push to the function:

```csharp
using System.Net;
using System.Text;
using Google.Cloud.Functions.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orders.Tests;

public class CloudFunctionTests
{
    private const string Push = """
        {
          "message": {
            "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
            "messageId": "2070443601311540",
            "publishTime": "2026-09-23T19:13:55.749Z"
          },
          "subscription": "projects/my-project/subscriptions/orders"
        }
        """;

    [Fact]
    public async Task APushReachesTheHandlerThroughTheFunctionsFramework()
    {
        using var server = new FunctionTestServer<ApplicationCloudFunction>();
        using var client = server.CreateClient();

        using var response = await client.PostAsync(
            "/",
            new StringContent(Push, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = server.Server.Services.GetRequiredService<OrderLog>();

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The push's `data` is `{"id":"A-1","quantity":2}`, base64-encoded. The test is a plain xUnit
`[Fact]`. `Server.Services` is the function's own container, so the test reads the application's
singletons there rather than through a test parameter. Through the test server, a delivery that no
handler names answers 500, so the status assertion catches it.

## The repository's container tests

`Hardened.Simulators.slnx`, at the root of the repository, holds the tests that run the repository's
Cloud Run fixtures in a container. Its Google Cloud folder has six test projects under
`src/Clouds/Gcp/IntegrationTests`, and their shared harness, `Hardened.Gcp.CloudRun.Testing.Containers`.
The harness is not a package.

Each test mounts a fixture's build output into `mcr.microsoft.com/dotnet/aspnet:8.0`. It starts the
fixture as `dotnet <assembly>.dll` with `PORT=8080`, the way Cloud Run starts a service.

The queue and blob tests drive the service through the Pub/Sub emulator, in the image
`gcr.io/google.com/cloudsdktool/google-cloud-cli:583.0.0-emulators`. They publish with Google's own
Pub/Sub client, with `PUBSUB_EMULATOR_HOST` set. A push subscription on the emulator delivers to the
container. The blob test publishes the message a bucket's notification configuration would send, with
its attributes and metadata written by hand.

The timer, invocation, event and web tests post the request Cloud Scheduler, a caller, Eventarc or a
web client would send, from the test to the container.

The tests read what a handler did from the lines the fixture prints with the prefix
`HARDENED-OBSERVED `. A `[Mock]` cannot reach into the fixture's process.

| Test class | The fixture | What the tests send and check |
|---|---|---|
| `PubSubPushTests` | A queue handler | A message published to the emulator reaches the handler. A message the handler refuses is pushed again |
| `ShutdownTests` | The same queue handler | A push whose handler takes three seconds, with the container stopped half a second in: the push answers 200, and the process exits with 0 |
| `StorageNotificationTests` | A blob handler | A Cloud Storage notification published to the emulator reaches the handler |
| `ScheduledRequestTests` | A timer handler | A Cloud Scheduler request answers 200. A request whose job name is another timer's answers 500 |
| `InvocationTests` | A `[HardenedFunction]` | A `POST` to `/_triggers/invoke/Handle` answers with the handler's return value. A `POST` to `/Handle` answers 404 |
| `CloudEventRequestTests` | An `[Event]` handler | A binary-mode CloudEvent answers 200. An event no handler declares answers 500 |
| `HttpServiceImageTests` | Web routes | A `GET` and a `POST` answer 200. An unmatched path answers 404 |

`dotnet test` on one of the projects runs it. The tests need Docker. Without a Docker daemon they
fail rather than skip. This command runs the queue tests from the root of the repository:

```bash
dotnet test src/Clouds/Gcp/IntegrationTests/Queue/Hardened.IntegrationTests.CloudRunQueue.Simulator.Tests
```

## Next

- [Testing functions](/guide/testing-functions): the façades, `[FunctionTesting]`,
  `[PipelineDelivery]` and failed messages in a test
- [Test hosts](/guide/testing-hosts): `[KestrelRuntime]` and every test host
- [Blobs](/gcp/blob): a test that posts a Cloud Storage delete event by hand
- [Web services](/gcp/web): running the application as a Cloud Functions 2nd gen function
- [Overview](/gcp/): running a service locally, and deploying it
