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
| `Hardened.Aws.Lambda.Http` | `[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]`, behind an API Gateway HTTP API or REST API, a function URL or an Application Load Balancer | `[LambdaHttpModule]` | `HardenedHttpModule` |
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

services.AddLogging(builder =>
    builder
        .AddLambdaLogger(new LambdaLoggerOptions { IncludeException = true })
        .SetMinimumLevel(LogLevel.Information)
);

services.AddHardenedEnvironment(new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(services);

await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
```

The project is an executable, with `<OutputType>Exe</OutputType>`. Lambda starts a function whose
handler is the assembly's name only when the assembly has an entry point.

In a function, `LambdaEmulator.StartIfLocal` takes the application's type and no `apiGateway`
argument, so the AWS Lambda Test Tool runs without its API Gateway emulator.
[Hosts](/guide/hosts) covers `LambdaEmulator.StartIfLocal`, `AddLambdaLogger` and
`HardenedLambdaBootstrap.Run`. [Logging](#logging) covers what the function logs.
[Environments](/guide/environments) covers `AddHardenedEnvironment`.

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

## Running under SAM

`sam local invoke` and `sam local start-api`, from the AWS SAM CLI, run the function in Lambda's
runtime image. That image sets `AWS_LAMBDA_RUNTIME_API`, so the run differs from `dotnet run` in
two ways:

- `LambdaEmulator.StartIfLocal` starts nothing.
- The environment's name is `production` unless `HARDENED_ENVIRONMENT` is set.
  [Environments](/guide/environments) covers how the name is chosen.

A class registered with `[IfNotEnvironment("development", "test")]` is therefore the one that
serves. When that class is the real AWS store, the local run calls AWS with whatever credentials
the shell has. [Registering services](/guide/services#registering-by-environment) covers the
attributes.

`template.yaml` declares the variable from a parameter, so a local run can name the environment:

```yaml
AWSTemplateFormatVersion: "2010-09-09"
Transform: AWS::Serverless-2016-10-31

Parameters:
  HardenedEnvironment:
    Type: String
    Default: production

Resources:
  Orders:
    Type: AWS::Serverless::Function
    Properties:
      CodeUri: src/Orders
      Handler: Orders
      Runtime: dotnet8
      MemorySize: 1024
      Timeout: 30
      LoggingConfig:
        LogFormat: JSON
      Environment:
        Variables:
          HARDENED_ENVIRONMENT: !Ref HardenedEnvironment
```

`--parameter-overrides` names the environment for one run:

```bash
sam build
sam local invoke Orders --event event.json --parameter-overrides HardenedEnvironment=development
```

`--env-vars` sets it from a file instead. A file with `{"Orders": {"HARDENED_ENVIRONMENT": "development"}}`
works only because the template declares `HARDENED_ENVIRONMENT`. `--env-vars` overrides only
variables that the template declares, and ignores the rest.

`sam local start-api` serves a function that has an API event. For the Lambda host of
`dotnet new hardened-web --host aws-lambda`, the function's properties add one:

```yaml
      Events:
        Api:
          Type: HttpApi
```

`sam build` packages the function with Amazon.Lambda.Tools, which reads the target framework from
the project file or from `aws-lambda-tools-defaults.json` beside it. The templates set the framework
in `Directory.Build.props`, so they write that file into the `hardened-function` project and the
Lambda host:

```json
{
  "configuration": "Release",
  "framework": "net8.0",
  "function-architecture": "x86_64"
}
```

Without it, `sam build` fails with "Missing required parameter: --framework". A project made from an
earlier template has no such file, and adding this one fixes it.

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

## Logging

The template's `Program.cs` logs through `AddLambdaLogger` from `Amazon.Lambda.Logging.AspNetCore`.
Deploy the function with its log format set to JSON. Each entry is then one JSON record, and the
values in the entry's message are fields of their own. An invocation of the `Orders` function writes
this record when it starts:

```json
{"timestamp":"2026-09-28T00:26:23.843Z","level":"Information","requestId":"8476a536-e9f4-11e8-9739-2dfe598c3fcd","message":"[Hardened.Requests.Runtime.Logging.RequestLogger] QUEUE /orders started","httpMethod":"QUEUE","path":"/orders"}
```

A record that logs an exception also has `errorType`, `errorMessage` and `stackTrace`. A record
written at startup, before the first invocation, has no `requestId`.

The log format is a setting of the function. Lambda passes it to the function as
`AWS_LAMBDA_LOG_FORMAT`, and the Lambda runtime and the logger both read that variable.
[Deploying](#deploying) sets it with `--logging-config LogFormat=JSON`. In CloudFormation it is
`LogFormat` in the function's `LoggingConfig`. In the CDK it is `loggingFormat`.

A function left on Lambda's default text format writes each entry as a line of text. It logs this
warning at startup:

```text
2026-09-28T00:23:33.637Z		warn	[Warning] Hardened.Aws.Lambda.Runtime.Hosting.HardenedLambdaBootstrap: This function logs in Lambda's text format, because AWS_LAMBDA_LOG_FORMAT is not set. Each entry reaches CloudWatch as a line of text, and the values in a message are not fields of their own. Set the function's log format to JSON: LogFormat in its LoggingConfig, loggingFormat in the CDK, or --logging-config LogFormat=JSON with the AWS CLI.
```

In the text format, the logger leaves an exception out of the line unless `IncludeException` is
set. The template sets it.

`LambdaEmulator.StartIfLocal` sets `AWS_LAMBDA_LOG_FORMAT` to `JSON` for a local run, so a local run
writes the same records. Set `AWS_LAMBDA_LOG_FORMAT` to `Text` before the run to see lines of text.

The startup services run after the Lambda runtime has installed its log writer, so what they log is
written in the function's format. A startup service that throws stops the function, and the runtime
reports the exception to Lambda as an init error.

## Deploying

Hardened has no package for deploying to AWS. The AWS packages are the ones under
[Packages](#packages).

A function runs on Lambda's managed .NET 8 runtime, `dotnet8`. Its handler is the assembly's name
alone, such as `Orders`. `dotnet publish -c Release` writes `Orders.dll`,
`Orders.runtimeconfig.json`, `Orders.deps.json` and the package assemblies. A zip of the publish
output, with those files at its root, is the deployment package. `aws lambda create-function`
creates the function from it with `--runtime dotnet8` and `--handler Orders`.
`--logging-config LogFormat=JSON` sets its log format, which [Logging](#logging) covers. From the
solution directory, these commands publish the function, zip the output and create the function:

```bash
dotnet publish src/Orders -c Release -o publish
cd publish && zip -r ../function.zip . && cd ..
aws lambda create-function --function-name Orders \
    --runtime dotnet8 --handler Orders \
    --role arn:aws:iam::123456789012:role/orders-function \
    --logging-config LogFormat=JSON \
    --zip-file fileb://function.zip
```

On Lambda, `AWS_LAMBDA_RUNTIME_API` is set, so `LambdaEmulator.StartIfLocal` starts nothing.
[Hosts](/guide/hosts) covers it.

[Queues](/aws/queue), [Topics](/aws/topic) and [Invocations](/aws/invoke) cover connecting their
sources to a function. Queues also covers the permissions the execution role needs for a queue.
Topics covers the permission that lets SNS invoke the function. [Environments](/guide/environments)
covers naming the environment in a deployed function.

## Cold start

A new execution environment loads the runtime, JIT-compiles the code that `Program.cs` and the
startup services run, and then takes its first invocation. The templates set none of the settings
below. Hardened has no measurements of them, so this section gives each setting and what it costs,
and no figures. Measure a change with the `Init Duration` that Lambda reports for each cold start.

| Setting | Does | Costs |
|---|---|---|
| `PublishReadyToRun` | Compiles the application's assemblies to native code at publish, so less is JIT-compiled at startup and on each route's first request | Assemblies two to three times larger. The publish needs a `RuntimeIdentifier`. Tiered compilation later replaces the hot methods with JIT code |
| `TieredPGO` | Recompiles hot methods using the profile collected while they run | Nothing to turn on. It is on by default on .NET 8. A short-lived environment may end before it pays off |
| `InvariantGlobalization` | Starts without loading ICU, the globalization library | Every culture behaves as the invariant culture. Creating a named culture, such as `fr-FR`, throws `CultureNotFoundException` |
| SnapStart | Restores each new environment from a snapshot taken after initialization | Charges for caching and for each restore. Published versions and aliases only. State created during initialization is shared by every restored environment |
| Native AOT on `provided.al2023` | Runs a native binary with no JIT at all | A Linux build for each architecture, no SnapStart, and every JSON type declared in a context |

### ReadyToRun

The host project sets the properties. The runtime identifier names the function's architecture,
`linux-x64` for `x86_64` and `linux-arm64` for `arm64`:

```xml
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <SelfContained>false</SelfContained>
</PropertyGroup>
```

The function still runs on `dotnet8`, and [Deploying](#deploying) is otherwise unchanged. The .NET
SDK on Windows, macOS and Linux can compile ReadyToRun code for either Linux architecture. The .NET
runtime's own assemblies are already ReadyToRun, so the gain comes only from the application's
assemblies and its packages.

### Invariant globalization

```xml
<PropertyGroup>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

Hardened reads route, query, header and environment values with the invariant culture, so binding
does not change. Code of the application's own that formats or compares text for a culture does.

### SnapStart

SnapStart runs on the `dotnet8` runtime. It is set on the function with `SnapStart` and
`ApplyOn: PublishedVersions`. Lambda runs the initialization phase when a version is published and
takes the snapshot afterwards. `HardenedLambdaBootstrap.Run` runs the startup services in that
phase, so whatever they create is in the snapshot:

- A random value or an ID generated at startup is the same in every restored environment.
- A connection a startup service opened may be broken after a restore. AWS says that the
  connections an AWS SDK client opens usually resume.
- Credentials or timestamps read at startup are as old as the snapshot.

`SnapshotRestore` in `Amazon.Lambda.Core` registers code to run before the snapshot and after each
restore. Register it in `Program.cs`, before `HardenedLambdaBootstrap.Run`:

```csharp
using Amazon.Lambda.Core;

SnapshotRestore.RegisterAfterRestore(() =>
{
    // Reopen what a startup service opened.
    return ValueTask.CompletedTask;
});
```

The snapshot hooks are handled by `Amazon.Lambda.RuntimeSupport`, which runs Hardened's invocation
loop. A Hardened function has not yet been run under SnapStart.

SnapStart does not work with provisioned concurrency, Amazon EFS, ephemeral storage above 512 MB,
or an OS-only runtime such as `provided.al2023`.

### Native AOT on provided.al2023

A Native AOT function is a native executable named `bootstrap`, run on the OS-only runtime
`provided.al2023`. The host project sets:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <AssemblyName>bootstrap</AssemblyName>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>
```

The application module also needs `[AotSerializerModule]`, and the JSON context has to declare every
type the application reads or writes. [JSON serialization](/guide/json#native-aot) covers both. A
publish without them succeeds and then answers 500.

Native AOT does not compile across operating systems. Publish on Linux for the function's
architecture, such as in an `amazonlinux:2023` container, so that the binary links against the same
`glibc` as the runtime. Create the function with `--runtime provided.al2023`. Lambda runs
`bootstrap` and ignores the handler, so any value serves. A package that reads types by reflection
can fail at run time in a native binary even when the publish gives no warning.

## Next

- [Triggers](/guide/triggers): the trigger attributes, batches and the change feeds on every cloud
- [Invocations](/aws/invoke): direct invocations of a `[HardenedFunction]`
- [Queues](/aws/queue): SQS queues and batch failure reports
- [Web applications](/aws/lambda-web): web applications on Lambda
- [Testing](/aws/testing): testing on AWS
