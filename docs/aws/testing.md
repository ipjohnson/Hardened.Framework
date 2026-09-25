# Testing

`Hardened.Aws.Lambda.Testing` runs what a test sends through the function's
`LambdaInvocationHandler`, the class that runs every invocation of a deployed function.
`[LambdaTesting]` does it for trigger and `[HardenedFunction]` handlers, and `[LambdaWebTesting]`
does it for routes.

`dotnet new hardened-function -n Orders --trigger queue` writes this handler in
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

The scaffold's `tests/Orders.Tests/Bootstrap.cs` puts `[LambdaTesting]` beside `[FunctionTesting]`:

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

The first test in `tests/Orders.Tests/OrderHandlerTests.cs` sends one order:

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

`[LambdaTesting]` builds each façade call, such as `queues.Orders(...)`, into the event the AWS
source sends. It invokes the function's `LambdaInvocationHandler` with that event, so the AWS
adapter reads the event before the handler runs. In this test the SQS adapter reads an SQS event
with one record.

The attribute is in the package `Hardened.Aws.Lambda.Testing`, in the namespace of the same name.
It goes on the assembly, a class or a method. For the default `--host aws`, the `hardened-function`
template writes this `Bootstrap.cs` and the package reference. Under `[LambdaTesting]`, an
`ITriggerDelivery` parameter is `LambdaEnvelopeDelivery`.

[Testing functions](/guide/testing-functions) covers `[FunctionTesting]`, the façades, how the two
attributes combine, `[PipelineDelivery]`, and which containers a call builds.

## The event each trigger gets

`[LambdaTesting]` sends a different event for each trigger:

| Trigger | `[LambdaTesting]` sends | Values in the event |
|---|---|---|
| `[Queue]` | An SQS event with one record per message | Message ids `<queue>-0`, `<queue>-1` and so on, receipt handles `receipt-0`, `receipt-1` and so on, and the queue ARN `arn:aws:sqs:us-east-1:123456789012:<queue>`. No message attributes |
| `[Topic]` | An SNS event with one record per message | Message ids `<topic>-0` and so on, and the topic ARN `arn:aws:sns:us-east-1:123456789012:<topic>`. No subject and no message attributes |
| `[Timer]` | One EventBridge `Scheduled Event` from `aws.events` per call | Event id `<name>-fired`, time `2026-01-01T00:00:00Z`, an empty `detail`, and the rule ARN `arn:aws:events:us-east-1:123456789012:rule/<name>` |
| `[Change]` | A DynamoDB Streams event with one `MODIFY` record per message | The message as the new image and as the old image, stream view type `NEW_AND_OLD_IMAGES`, and ascending sequence numbers |
| `[Stream]` | A Kinesis event with one record per message, the message base64-encoded in `data` | Partition keys `<stream>-0` and so on, and ascending sequence numbers |
| `[Blob]` | An S3 event with one `ObjectCreated:Put` record per message, whatever the message's `eventName` | The bucket named by the source, the message's `key` or else `object-0` and so on, and the message's `size` or else 0 |
| `[HardenedFunction]` | The message itself as JSON, with no event around it | The handler's own name as the function's name |
| `[Event]` | Nothing. The call throws `NotSupportedException` | |

Every ARN names the region `us-east-1` and the account `123456789012`.

[Events](/aws/event) covers sending an EventBridge event in a test by invoking
`LambdaInvocationHandler`, as the test in [Reading a batch report](#reading-a-batch-report) does
with an SQS event.

## What a handler sees

A handler and its test see these differences between `[LambdaTesting]` and `[FunctionTesting]`
alone:

| | Under `[LambdaTesting]` | Under `[FunctionTesting]` alone |
|---|---|---|
| Headers on a trigger handler's request | The headers its adapter gives it | None of the adapters' headers |
| What `[NewImage]` and `[OldImage]` bind | The same item, in DynamoDB's attribute-value form | Binding either one fails with `InvalidOperationException` |
| What a `[Blob]` handler binds | The S3 adapter's body. `bucket` is the source's name, and `eTag`, `sequencer`, `eventName` and `eventTime` are set | The message as the test wrote it |
| A handler's `CancellationToken` | Cancelled about 29.5 seconds into a call, because the invocation's Lambda context reports 30 seconds remaining | `CancellationToken.None` |
| A `[Timer]` or `[HardenedFunction]` handler that throws | The call throws the handler's exception | [Testing functions](/guide/testing-functions) covers it |

Under `[FunctionTesting]` alone, binding `[NewImage]` fails with this message:

```text
[NewImage] was bound on a handler that is not serving a DynamoDB change. It reads the stream record off the request, so it only works under [Change].
```

A change handler that binds either image is tested under `[LambdaTesting]`. [Changes](/aws/change)
covers a handler whose only parameters are images, which its façade cannot call.

[Queues](/aws/queue), [Topics](/aws/topic), [Timers](/aws/timer), [Changes](/aws/change),
[Streams](/aws/stream) and [Blobs](/aws/blob) list the headers each adapter gives a handler. The AWS
[Overview](/aws/) covers the invocation deadline.

## Reading a batch report

A façade call discards what the invocation answers, so it never returns the `batchItemFailures`
report. A test reads the report by invoking `LambdaInvocationHandler` itself, with the event written
out. The class is in the namespace `Hardened.Aws.Lambda.Runtime.Hosting`. A test takes it as a
parameter, like any service in the application's container.

The application in `src/Orders/Application.cs` sets `ReportBatchItemFailures` on `[SqsModule]`:

```csharp
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

The handler in `src/Orders/OrderHandler.cs` refuses an order with a negative quantity:

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

The test in `tests/Orders.Tests/BatchReportTests.cs` sends two messages, and the handler refuses the
second:

```csharp
using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using DependencyModules.xUnit.Attributes;
using Hardened.Aws.Lambda.Runtime.Hosting;
using NSubstitute;
using Xunit;

namespace Orders.Tests;

public class BatchReportTests
{
    private const string SqsEvent = """
        {"Records":[
          {"messageId":"m-1","receiptHandle":"r-1","body":"{\"id\":\"A-1\",\"quantity\":2}",
           "eventSource":"aws:sqs","eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders"},
          {"messageId":"m-2","receiptHandle":"r-2","body":"{\"id\":\"A-2\",\"quantity\":-1}",
           "eventSource":"aws:sqs","eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders"}
        ]}
        """;

    [ModuleTest]
    public async Task TheReportNamesTheRefusedMessage(LambdaInvocationHandler handler, OrderLog log)
    {
        var context = Substitute.For<ILambdaContext>();
        context.RemainingTime.Returns(TimeSpan.FromSeconds(30));

        var output = await handler.Invoke(new MemoryStream(Encoding.UTF8.GetBytes(SqsEvent)), context);

        using var report = await JsonDocument.ParseAsync(output);

        var failed = report.RootElement
            .GetProperty("batchItemFailures")
            .EnumerateArray()
            .Select(item => item.GetProperty("itemIdentifier").GetString());

        Assert.Equal(["m-2"], failed);
        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

`Invoke(Stream input, ILambdaContext lambdaContext)` runs one invocation with the event's bytes. It
returns the function's answer as a `Stream`. The invocation runs in the test's own container, so the
test's `OrderLog` holds what the handler recorded.

`ILambdaContext` is in `Amazon.Lambda.Core`, which the testing package brings. The test uses an
NSubstitute substitute for it. The substitute sets `RemainingTime`. With 500 milliseconds or less
remaining, the handler's `CancellationToken` is cancelled before the handler starts.

With `ReportBatchItemFailures` set, the answer names the `messageId` of each message whose handler
threw. The other messages are handled. Without it, `Invoke` throws the handler's exception. The test
passes with `[LambdaTesting]` in `Bootstrap.cs` and without it.

Kinesis and DynamoDB Streams events answer the same report, with sequence numbers as
`itemIdentifier`. [Queues](/aws/queue) covers `ReportBatchItemFailures` and the event source mapping
it needs. [Streams](/aws/stream) and [Changes](/aws/change) cover their reports.

## Testing a web application

`[LambdaWebTesting]` sends each request a web test makes to the function's
`LambdaInvocationHandler`, as an API Gateway payload format 2.0 event. It reads the function's
answer back as the test's response. `LambdaWebTestingAttribute` is in the namespace
`Hardened.Aws.Lambda.Testing`, in the same package as `[LambdaTesting]`. It goes on a method, a
class or the assembly.

`dotnet new hardened-web --host aws-lambda` does not reference `Hardened.Aws.Lambda.Testing` in its
test project. It writes no Lambda test host. [Test hosts](/guide/testing-hosts) shows the two lines
to add and a test class. The testing package references every AWS adapter package,
`Hardened.Aws.Lambda.Http` among them.

The template's test project names the library module, `TodosLibrary`, as its entry point, so
`[LambdaHttpModule]` goes beside `[LambdaWebTesting]`. Without it, every request fails with this
message:

```text
No service for type 'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been registered.
```

`[LambdaWebTesting]` needs `[assembly: WebTesting]`, which the template's `Bootstrap.cs` declares.
Without it, a test that takes `ITestWebApp` fails with this message:

```text
Instances of abstract classes cannot be created.
```

Without `ResponseMode`, the function answers in `buffered` mode. Each request runs in a container of
its own. [Writing a test](/guide/testing) covers containers and `[Shared]`. A path with no route
answers 404. The invocation's Lambda context reports 30 seconds remaining, so a handler's
`CancellationToken` is cancelled about 29.5 seconds into a request.

The attribute builds this event from each request:

| Event field | Value |
|---|---|
| `requestContext.http.method`, `rawPath`, `rawQueryString`, `headers` | From the test's request |
| `queryStringParameters` | The query string's parameters, percent-decoded |
| `body` | The request body as text when its content type is `text/*`, `application/json`, `application/xml`, `application/javascript`, `application/x-www-form-urlencoded`, `*+json` or `*+xml`, or when it has none. Otherwise base64, with `isBase64Encoded` set to `true` |
| `requestContext.http.sourceIp` | `203.0.113.7` |
| `requestContext.domainName` | `apigateway.test` |
| `cookies` | Not sent |
| `requestContext.stage` | Not sent |

## What a web test sees

A `Cookie` header set in the test goes into the event's `headers` and not into `cookies`, so
`[FromCookie]` binds nothing. In `buffered` mode the test host reads `statusCode`, `headers` and
`body` from the function's answer. It does not read `cookies` or `isBase64Encoded`.

A test sees these differences between the two modes:

| | `buffered`, the default | `ResponseMode = LambdaResponseMode.Stream` |
|---|---|---|
| A cookie the handler sets | Missing from the response | `Set-Cookie` |
| A body marked binary, a compressed body among them | Its base64 text | The bytes the handler wrote |
| An `IResponseStreamFactory` parameter | `RuntimeResponseStreamFactory` | `StreamedResponseCapture` |

A response compressed by `[Compress]` is marked binary, so in `buffered` mode `Deserialize<T>()` on
it throws `InvalidDataException` with this message:

```text
The archive entry was compressed using an unsupported compression method.
```

A request sent with `Accept-Encoding: identity` gets the body uncompressed.

`[Grants]` and `[Subject]` are not applied. A test marked `[Grants("todos:read")]` gets 401 from an
`[AuthorizeGrants("todos:read")]` handler. `LastResponse` is not recorded.

A response read through an `HttpClient` has no `Content-Type`, so a typed client fails. The
`hardened-web` template's Kiota test fails with this message:

```text
The response declares a body of List`1 and carried none.
```

[Test hosts](/guide/testing-hosts) covers these limits with the other hosts.

## Testing a streamed response

`[LambdaWebTesting(ResponseMode = LambdaResponseMode.Stream)]` runs each invocation in `stream` mode.
A function URL in `RESPONSE_STREAM` invoke mode needs this mode. [Web applications](/aws/lambda-web)
covers the mode and what differs in it.

In this mode the attribute registers a `StreamedResponseCapture` as the application's
`IResponseStreamFactory`. The invocation writes the response to the capture. The test host builds
the test's response from the capture: the status, headers and cookies the stream opened with, and
the bytes written to it. A test takes `IResponseStreamFactory` as a parameter to read the capture.

A test reads these members of the capture:

| Member | Holds |
|---|---|
| `Opened` | Whether the invocation opened a Lambda response stream |
| `Prelude` | What the stream opened with: its `StatusCode`, `Headers` and `Cookies`. Null when no stream opened |
| `Body` | Every byte written to the stream, as a `byte[]` |

`LambdaResponseMode` and `IResponseStreamFactory` are in `Hardened.Aws.Lambda.Runtime.Streaming`.
`StreamedResponseCapture` is in `Hardened.Aws.Lambda.Testing`. `Prelude` is Amazon's
`HttpResponseStreamPrelude`, from `Amazon.Lambda.Core.ResponseStreaming`.

This controller, added to the scaffold's library as `src/Todos/TodoEventsController.cs`, answers
with an event stream. The library's `[BasePath("/todos")]` puts it at `/todos/events`.

```csharp
using System.Runtime.CompilerServices;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoEventsController
{
    [Get("/events")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Todo> Events(
        ITodoStore store,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var todo in await store.All())
        {
            yield return todo;
        }
    }
}
```

This test, in `tests/Todos.Tests/TodoStreamTests.cs`, runs in `stream` mode and reads the capture:

```csharp
using System.Net;
using DependencyModules.xUnit.Attributes;
using Hardened.Aws.Lambda.Http;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Aws.Lambda.Testing;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

[LambdaWebTesting(ResponseMode = LambdaResponseMode.Stream)]
[LambdaHttpModule]
public class TodoStreamTests
{
    [ModuleTest]
    public async Task TheEventsLeaveOnALambdaResponseStream(
        ITestWebApp app,
        IResponseStreamFactory streams
    )
    {
        var response = await app.Get("/todos/events");

        response.Assert.Ok();
        Assert.StartsWith("data: {\"id\":1,", await response.ReadTextAsync());

        var capture = Assert.IsType<StreamedResponseCapture>(streams);

        Assert.True(capture.Opened);
        Assert.Equal(HttpStatusCode.OK, capture.Prelude!.StatusCode);
        Assert.Equal("text/event-stream", capture.Prelude.Headers["Content-Type"]);
    }
}
```

::: warning
An event stream answers the same status, headers and frames in `buffered` mode. A test that asserts
only on the response passes without `ResponseMode = LambdaResponseMode.Stream`, although its
invocation never streams. Assert on `StreamedResponseCapture.Opened` as well.
:::

The capture holds one invocation. The test host resets it before each request and reads it after,
so requests that a test sends at the same time share it.

Neither the AWS Lambda Test Tool nor the Runtime Interface Emulator streams a response to its
caller. A test with `ResponseMode` is the way to run `stream` mode before a deployment.
[Web applications](/aws/lambda-web) covers the Test Tool. The next section covers the emulator.

## Running a function in the Lambda base image

AWS's image `public.ecr.aws/lambda/dotnet:8` runs a function in Lambda's managed .NET 8 runtime.
The Runtime Interface Emulator stands in front of the function, in place of the Lambda service. The
function's publish output is mounted at `/var/task`. The container's command is the handler, which
is the assembly's name.

From the solution directory of the `Orders` application in
[Reading a batch report](#reading-a-batch-report):

```bash
dotnet publish src/Orders -c Release -o publish
docker run --rm -p 9000:8080 -v "$PWD/publish:/var/task:ro" public.ecr.aws/lambda/dotnet:8 Orders
```

The emulator listens on port 8080 in the container. The function's log lines go to the container's
output. The emulator answers these requests:

| Request | Answer |
|---|---|
| `POST /2015-03-31/functions/function/invocations` | One invocation. The body is the event, and the answer is what the function returned |
| `POST /2021-11-15/functions/function/response-streaming-invocations` | 404. The emulator has no response-streaming endpoint |
| Any other path | 404. A request to one shows that the emulator is listening, without using an invocation |

In a second terminal, with the event saved as `event.json`:

```bash
curl -X POST http://localhost:9000/2015-03-31/functions/function/invocations --data-binary @event.json
```

The event that `BatchReportTests` sends gets the same report from the published function:

```http
POST /2015-03-31/functions/function/invocations

{"Records":[
  {"messageId":"m-1","receiptHandle":"r-1","body":"{\"id\":\"A-1\",\"quantity\":2}",
   "eventSource":"aws:sqs","eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders"},
  {"messageId":"m-2","receiptHandle":"r-2","body":"{\"id\":\"A-2\",\"quantity\":-1}",
   "eventSource":"aws:sqs","eventSourceARN":"arn:aws:sqs:us-east-1:123456789012:orders"}
]}

HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8

{"batchItemFailures":[{"itemIdentifier":"m-2"}]}
```

A failed invocation answers 200, with a JSON body that has `errorType`, `errorMessage` and
`stackTrace`. The emulator sends no `X-Amz-Function-Error` header.

A web function takes an API Gateway payload format 2.0 event. It answers with its payload format
2.0 response as JSON.

The emulator names the function `test_function`. A `[HardenedFunction]` with a name answers only
when `AWS_LAMBDA_FUNCTION_NAME` names it. Set the variable with `-e AWS_LAMBDA_FUNCTION_NAME=<name>`
on `docker run`. [Invocations](/aws/invoke) covers how a function name picks the handler.

Send each invocation once. The emulator holds one slot for the invocation it is serving. A second
POST while the slot is held fails with `ReserveFailed: AlreadyReserved` and stops the emulator. The
first request gets no answer. AWS tracks the failure as
[aws-lambda-runtime-interface-emulator#97](https://github.com/aws/aws-lambda-runtime-interface-emulator/issues/97),
titled `Runtime error: invalid memory address or nil pointer dereference`. The issue is open.

AWS documents the emulator in its repository,
[aws-lambda-runtime-interface-emulator](https://github.com/aws/aws-lambda-runtime-interface-emulator).
The AWS [Overview](/aws/) covers publishing a function and deploying it.

## The repository's emulator tests

`Hardened.Simulators.slnx`, at the root of the repository, holds the tests that run functions in
their cloud's own image. Its AWS project is
`src/Clouds/Aws/IntegrationTests/Rie/Hardened.IntegrationTests.Rie.Tests`. The project runs four of
the repository's Lambda applications in `public.ecr.aws/lambda/dotnet:8`. Each application starts
the way a deployed function starts. The tests invoke it through the emulator.

| Test class | The function | What the tests send |
|---|---|---|
| `RuntimeImageTests` | A queue handler | An SQS message, handled with an empty report, and one the handler refuses, which fails the invocation |
| `DirectInvokeImageTests` | A `[HardenedFunction]` with no name, and `AWS_LAMBDA_FUNCTION_NAME` set to its method's name | A payload, whose answer is the handler's return value |
| `EventFamilyImageTests` | A queue, a topic, a timer and an `[Event]` handler in one function | An SQS, an SNS, a scheduled and an EventBridge event, each reaching its handler |
| `LambdaHttpImageTests` | Web routes | A GET and a POST as payload format 2.0 events, answered 200, and an unmatched path, answered 404 |

A `[Mock]` cannot reach into another process. The tests read what a handler did from lines it
prints with the prefix `HARDENED-OBSERVED` and a space.

This command, from the root of the repository, runs them:

```bash
dotnet test src/Clouds/Aws/IntegrationTests/Rie/Hardened.IntegrationTests.Rie.Tests
```

The tests need Docker. Without a Docker daemon they fail rather than skip. Their harness,
`Hardened.Functions.Testing.Containers`, is not a package.

## Next

- [Testing functions](/guide/testing-functions): the façades, `[FunctionTesting]`,
  `[PipelineDelivery]` and failed messages in a test
- [Test hosts](/guide/testing-hosts): every test host, and adding `[LambdaWebTesting]` to a test
  project
- [Web applications](/aws/lambda-web): the Lambda response mode, and what differs in `stream` mode
- [DynamoDB client](/aws/dynamodb): DynamoDB Local in a test, with `[LocalDynamoDb]`
- [Overview](/aws/): running a function locally, and deploying it
