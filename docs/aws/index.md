# AWS

A Hardened function runs on AWS Lambda with `Hardened.Aws.Lambda.Runtime` and one adapter package for
each trigger its handlers use. The handler and the application class name no AWS service.

With `Hardened.Aws.Lambda.Sqs` referenced, `[Queue("orders")]` in `src/Orders/OrderHandler.cs`
receives the messages of the SQS queue named `orders`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

`src/Orders/Application.cs` holds the application class:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

While the function runs locally, an SQS event posted to it gets this response:

```http
POST /2015-03-31/functions/Orders/invocations
Content-Type: application/json

{
  "Records": [
    {
      "messageId": "059f36b4-87a3-44ab-83d2-661975830a7d",
      "receiptHandle": "AQEBwJnKyrHigUMZj6rYigCgxlaS3SLy0a",
      "body": "{\"id\":\"A-1\",\"quantity\":2}",
      "eventSource": "aws:sqs",
      "eventSourceARN": "arn:aws:sqs:us-east-1:123456789012:orders",
      "awsRegion": "us-east-1"
    }
  ]
}

HTTP/1.1 200 OK
Content-Type: application/json

{"batchItemFailures":[]}
```

`dotnet new hardened-function` writes a Lambda function with tests. `--trigger` picks the trigger.
Its default is `invoke`. The files above come from
`dotnet new hardened-function -n Orders --trigger queue`, without their comments.
[Project templates](/guide/project-templates) covers the options.

[Triggers](/guide/triggers) covers the trigger attributes and what they bind on every cloud. A web
application on Lambda is a different project shape. [Hosts](/guide/hosts) and
[Web applications](/aws/lambda-web) cover it.

## Packages

AWS has an adapter for every trigger.

| Package | Serves | Module attribute | Build property |
|---|---|---|---|
| `Hardened.Aws.Lambda.Runtime` | The invocation loop, `HardenedLambdaBootstrap` and `LambdaEmulator`. Every Lambda project references it | None. Each adapter's module includes the runtime | None |
| `Hardened.Aws.Lambda.Http` | `[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]`, behind an API Gateway HTTP API or a function URL | `[LambdaHttpModule]` | `HardenedHttpModule` |
| `Hardened.Aws.Lambda.Invoke` | `[HardenedFunction]`, invoked directly | `[InvokeModule]` | `HardenedInvokeModule` |
| `Hardened.Aws.Lambda.Sqs` | `[Queue]`, from Amazon SQS | `[SqsModule]` | `HardenedQueueModule` |
| `Hardened.Aws.Lambda.Sns` | `[Topic]`, from Amazon SNS | `[SnsModule]` | `HardenedTopicModule` |
| `Hardened.Aws.Lambda.EventBridge` | `[Timer]` and `[Event]`, from Amazon EventBridge | `[EventBridgeModule]` | `HardenedTimerModule` and `HardenedEventModule` |
| `Hardened.Aws.Lambda.DynamoDb` | `[Change]`, from DynamoDB Streams | `[DynamoDbStreamsModule]` | `HardenedChangeModule` |
| `Hardened.Aws.Lambda.Kinesis` | `[Stream]`, from Kinesis Data Streams | `[KinesisModule]` | `HardenedStreamModule` |
| `Hardened.Aws.Lambda.S3` | `[Blob]`, from S3 object notifications | `[S3Module]` | `HardenedBlobModule` |
| `Hardened.Aws.Lambda` | Every package above, in one reference | Every attribute above | Every property above |
| `Hardened.Aws.Lambda.Testing` | `[LambdaTesting]` and `[LambdaWebTesting]`, in a test project | None | None |

An application does not write a module attribute to get the adapter. The build registers the module
from the build property. [Triggers](/guide/triggers) covers the mechanism.

An application writes a module attribute to change a setting, such as
`[SqsModule(ReportBatchItemFailures = true)]`. The build then leaves its own registration out. Each
module attribute is in the namespace named like its package. `[SqsModule]` is in
`Hardened.Aws.Lambda.Sqs`.

The `Orders` function references these packages:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Requests.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Aws.Lambda.Sqs" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Amazon.Lambda.Logging.AspNetCore" Version="5.0.1" />
  <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Function.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
</ItemGroup>
```

The template's function also references `Amazon.Lambda.Logging.AspNetCore`, for the logger that
`Program.cs` installs. The template pins it at 5.0.1.

Each adapter package brings its own AWS event assembly. The `Orders` function's publish output holds
`Amazon.Lambda.SQSEvents.dll` and no other event assembly. `Hardened.Aws.Lambda` puts every adapter
in the deployment, whatever the handlers use. The build notes each unused one as `HRDF003`.
[Triggers](/guide/triggers) covers `HRDF003`.

`Hardened.Aws.DynamoDbClient` supplies DynamoDB clients to an application on any host.
[DynamoDB client](/aws/dynamodb) covers it.

## The application class

A function's application class is a `partial` class marked `[HardenedModule]`, with no AWS
attribute, like `Application` at the top of this page. The build registers the module of each
trigger the handlers use, in the generated `Application.TriggerModules.cs`.

One Lambda function serves one kind of handler. Trigger handlers can share a function with each
other. They cannot share one with web routes or with a `[HardenedFunction]`.

| Handlers in one function | What happens |
|---|---|
| Any mix of `[Queue]`, `[Topic]`, `[Timer]`, `[Event]`, `[Change]`, `[Stream]` and `[Blob]` | Each event reaches its handler |
| Web routes and trigger handlers | The build succeeds, and the first invocation fails with `This function declares more than one kind of handler - WebExecutionHandlerService, FunctionDispatchFilter. Web routes and function triggers are separate families and cannot share one function: an HTTP route answers a caller waiting on a connection, and a trigger fails the invocation to make its source redeliver. Split them into two functions.` |
| `[HardenedFunction]` and trigger handlers | [Invocations](/aws/invoke) covers this case |

A web application on Lambda names `[LambdaHttpModule]` on its application class.
[Hosts](/guide/hosts) covers it.

## The entry point

The template writes `src/Orders/Program.cs`. Nothing generates it.

```csharp
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orders;

using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application));

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddLambdaLogger().SetMinimumLevel(LogLevel.Information));

services.AddHardenedEnvironment(new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
```

The project is an executable, with `<OutputType>Exe</OutputType>`. Lambda starts a function whose
handler is the assembly's name only when the assembly has an entry point.

In a function, `LambdaEmulator.StartIfLocal` takes the application's type and no `apiGateway`
argument, so the AWS Lambda Test Tool runs without its API Gateway emulator.
[Hosts](/guide/hosts) covers `LambdaEmulator.StartIfLocal`, `AddLambdaLogger` and
`HardenedLambdaBootstrap.Run`. [Environments](/guide/environments) covers `AddHardenedEnvironment`.

The function starts without creating any handler. When a handler needs a service that nothing
registers, the function still starts. The first invocation that reaches that handler fails. A
function with no adapter module registered stops at startup with
`No service for type 'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been registered.`
[Triggers](/guide/triggers) covers how that happens.

## Cancellation

A handler receives the invocation's token as a `CancellationToken` parameter.
[Parameter binding](/guide/parameter-binding) covers it.

Each invocation's `CancellationToken` is cancelled 500 ms before the invocation's deadline. With less
than 500 ms left, the handler still runs. Its token is already cancelled when the handler starts. A
handler that lets the `OperationCanceledException` escape fails the invocation.

## Running locally

`dotnet run --project src/Orders` starts the AWS Lambda Test Tool and points the function at it.

```console
$ dotnet run --project src/Orders
Started the AWS Lambda Test Tool on http://localhost:5050
```

The tool's page at `http://localhost:5050` invokes the function with a payload typed into it. A POST
to `/2015-03-31/functions/Orders/invocations` on the same port invokes the function from a script or
`curl`. `Orders` is the function's name, which is its assembly's name. A trigger handler is invoked
with the event JSON its source sends.

The AWS CLI invokes the function too, with `--endpoint-url` set to the tool. The CLI signs the
request, so it needs a region and credentials. The tool accepts any values, such as
`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` set to `local` and `AWS_REGION` set to `us-east-1`. In
this command, `event.json` holds the request body of the exchange at the top of this page, and the
function's response is written to `response.json`:

```console
$ aws lambda invoke --endpoint-url http://localhost:5050 --function-name Orders \
    --cli-binary-format raw-in-base64-out --payload file://event.json response.json
{
    "StatusCode": 200
}
```

The tool answers a failed invocation with the header `X-Amz-Function-Error`. The header's value is the
exception's type. The body is JSON with `errorType`, `errorMessage` and `stackTrace`.

[Hosts](/guide/hosts) covers the tool's ports, its pin in `.config/dotnet-tools.json` and the build's
`dotnet tool restore`.

The tests invoke the function through its adapters with no tool running.
[Testing functions](/guide/testing-functions) and [Testing](/aws/testing) cover them.

## Failures and redelivery

On every trigger, a handler that throws fails the invocation. A web route answers 500 when its
handler throws. The invocation succeeds.

SQS, DynamoDB Streams and Kinesis invoke the function through an event source mapping. SNS, S3 and
EventBridge invoke it asynchronously. API Gateway and a direct caller invoke it synchronously. Lambda
retries a failed asynchronous invocation twice by default.

| Trigger | When a handler throws | What happens next | Page |
|---|---|---|---|
| `[Get]` and the other verbs | The route answers 500, and the invocation succeeds | API Gateway or the function URL returns the 500 to the caller | [Web applications](/aws/lambda-web) |
| `[HardenedFunction]` | The invocation fails | The caller gets the error. Lambda does not retry a direct invocation | [Invocations](/aws/invoke) |
| `[Queue]` | The invocation fails at the first failed message, and the messages after it do not run. With `[SqsModule(ReportBatchItemFailures = true)]`, every message runs and the response names the failed ones | The failed messages, or without reporting the whole batch, return to the queue. The queue's visibility timeout and redrive policy decide when they return and where a message goes after repeated failures | [Queues](/aws/queue) |
| `[Topic]` | The invocation fails | Lambda retries the invocation twice, then sends the event to the function's dead-letter queue or on-failure destination, if it has one | [Topics](/aws/topic) |
| `[Timer]` | The invocation fails | Lambda retries the invocation twice, then sends the event to the function's dead-letter queue or on-failure destination, if it has one | [Timers](/aws/timer) |
| `[Event]` | The invocation fails | Lambda retries the invocation twice, then sends the event to the function's dead-letter queue or on-failure destination, if it has one | [Events](/aws/event) |
| `[Change]` | The invocation fails at the first failed record, and the records after it do not run. With `[DynamoDbStreamsModule(ReportBatchItemFailures = true)]`, the response names that record | Lambda retries the batch, from the named record when there is one. The shard waits until the batch succeeds or its records expire | [Changes](/aws/change) |
| `[Stream]` | The invocation fails at the first failed record, and the records after it do not run. With `[KinesisModule(ReportBatchItemFailures = true)]`, the response names that record | Lambda retries the batch, from the named record when there is one. The shard waits until the batch succeeds or its records expire | [Streams](/aws/stream) |
| `[Blob]` | The invocation fails at the first failed notification | Lambda retries the invocation twice, then sends the event to the function's dead-letter queue or on-failure destination, if it has one | [Blobs](/aws/blob) |

With `ReportBatchItemFailures` on, an invocation with failed items succeeds. Its response names those
items. `ReportBatchItemFailures` on a module has to match the source's event source mapping. The mapping
turns this reporting on with the response type `ReportBatchItemFailures`. [Queues](/aws/queue) shows
the response and covers the mapping.

[Triggers](/guide/triggers) covers what a batch does when one item fails, and `BatchFailureMode`.

## Deploying

Hardened has no package for deploying to AWS. The AWS packages are the ones under
[Packages](#packages).

A function runs on Lambda's managed .NET 8 runtime, `dotnet8`. Its handler is the assembly's name
alone, such as `Orders`. `dotnet publish -c Release` writes `Orders.dll`,
`Orders.runtimeconfig.json`, `Orders.deps.json` and the package assemblies. A zip of the publish
output, with those files at its root, is the deployment package. `aws lambda create-function`
creates the function from it with `--runtime dotnet8` and `--handler Orders`. From the solution
directory, these commands publish the function, zip the output and create the function:

```bash
dotnet publish src/Orders -c Release -o publish
cd publish && zip -r ../function.zip . && cd ..
aws lambda create-function --function-name Orders \
    --runtime dotnet8 --handler Orders \
    --role arn:aws:iam::123456789012:role/orders-function \
    --zip-file fileb://function.zip
```

On Lambda, `AWS_LAMBDA_RUNTIME_API` is set, so `LambdaEmulator.StartIfLocal` starts nothing.
[Hosts](/guide/hosts) covers it.

[Queues](/aws/queue), [Topics](/aws/topic) and [Invocations](/aws/invoke) cover connecting their
sources to a function. Queues also covers the permissions the execution role needs for a queue.
Topics covers the permission that lets SNS invoke the function. [Environments](/guide/environments)
covers naming the environment in a deployed function.

## Next

- [Triggers](/guide/triggers): the trigger attributes, batches and the change feeds on every cloud
- [Invocations](/aws/invoke): direct invocations of a `[HardenedFunction]`
- [Queues](/aws/queue): SQS queues and batch failure reports
- [Web applications](/aws/lambda-web): web applications on Lambda
- [Testing](/aws/testing): testing on AWS
