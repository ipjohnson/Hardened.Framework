# Lambda application types

Every Hardened.Amz application is one entry point class carrying `[HardenedModule]` and exactly one
transport module attribute. The transport module registers the runtime services and selects which
bootstrap the source generator emits. The attribute on the class is the whole decision.

| Transport | Module attribute | Generator package |
|---|---|---|
| API Gateway or function URL | `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.SourceGenerator` |
| Direct invoke | `[LambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |
| SQS batch | `[LambdaFunctionModule]` + `[SqsLambda]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |
| DynamoDB Streams | `[LambdaFunctionModule]` + `[DynamoStreamLambda]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |

Response streaming is not a transport. It is a deployment setting on the same two hosts; see
[Response mode](#response-mode).

SQS and DynamoDB Streams take a second attribute because they are event sources layered on the
direct-invoke path rather than transports of their own. `[SqsLambda]` and `[DynamoStreamLambda]` add
batch handling; `[LambdaFunctionModule]` underneath brings the invocation path and the request
pipeline.

## Five rules

1. **`[HardenedModule]` is always required.** It is what marks a class as an entry point. Both
   generators ignore a class without it, so a transport module on its own produces nothing.
2. **The attribute goes on the application class itself.** Attributes on members or nested types
   select nothing.
3. **One transport module per application.** A web application and a function application are
   different generators; a class carries one or the other.
4. **Reference the matching source generator as an `Analyzer`**, with
   `ReferenceOutputAssembly="false"`. Without it nothing is generated and the application has no
   `Invoke` or `Main` at all.
5. **The module is the opt-in.** Applying it registers the services *and* selects the bootstrap.
   If an application compiles but throws on construction, a module attribute is missing — see
   [Failures](#failures).

## API Gateway or function URL

```csharp
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[LambdaWebModule]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => new() { Id = id };
}
```

The generated class has two entry points. `Main` runs the application on
`Amazon.Lambda.RuntimeSupport`'s bootstrap and is what a deployed function starts; see
[Project shape](#project-shape). `Invoke(request, context)`, taking an
`APIGatewayHttpApiV2ProxyRequest`, is the buffered handler: it is what tests and the local harness
drive, and a class library deployed with an `Assembly::Type::Invoke` handler on the managed
runtime keeps working through it.

Payload format 2.0 only. `[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]`
selects REST API payload format 1.0, which is not implemented and is a build error (`HRDAWS001`).

## Response mode

Every response leaves the function in one of two ways, and the deployment decides which.

| `HARDENED_LAMBDA_RESPONSE_MODE` | The function sends | Front doors that accept it |
|---|---|---|
| `buffered` (default) | The payload format 2.0 JSON, when the handler returns | API Gateway HTTP API; a function URL in `BUFFERED` invoke mode |
| `stream` | A prelude of status, headers and cookies, then the body as it is produced | A function URL in `RESPONSE_STREAM` invoke mode, with or without CloudFront in front |

The variable is read once, at startup. It is a deployment setting rather than an attribute because
the front doors are strict and the function cannot tell them apart: a `RESPONSE_STREAM` URL answers
a plain payload with a 500, a buffered front door drops the body of a streamed response, and the
event is the same document either way. An unrecognised value fails the application at startup.
An application can also set it in code:

```csharp
private void Configure(IAppConfig config) {
    config.Amend((LambdaResponseModeConfiguration mode) => mode.Mode = LambdaResponseMode.Stream);
}
```

Under `stream` the pipeline does not change and does not know which operations stream. The
response body is a stream that opens the Lambda response stream at its first byte, with whatever
status and headers the pipeline had decided by then. A buffered operation is one write and a
close; a handler returning `IAsyncEnumerable<T>` is a write per item, flushed as each item is
produced; a refusal opens the stream with the refusal's status and a JSON body, so an
`EventSource` stops rather than reconnecting forever. A response that ends with nothing written
still opens the stream and writes a newline, because a streamed response with an empty body
leaves CloudFront waiting.

Errors follow the same rule. Before the first byte the pipeline serializes the error as usual and
that byte opens the stream with the error's status: the client gets a complete, correctly typed
response. After the first byte the exception reaches the bootstrap, which writes it as trailers
and records the invocation as failed; the client sees a truncated stream and, for an event stream,
reconnects with `Last-Event-ID`.

An application with `[ServerSentEvents]` handlers deployed in buffered mode logs a warning at
startup naming them. Their events would be delivered when the invocation ends, which is not an
event stream. The build cannot refuse the combination, because the build does not know the
deployment.

`Hardened.Amz.Cdk` writes the variable and the invoke mode from one setting so they cannot
disagree:

```csharp
var (function, url) = lambdaCdkUtil.FunctionUrlFunctionCreate(new FunctionUrlLambdaRequest {
    Name = "orders",
    ApplicationType = typeof(Application),
    ResponseMode = LambdaResponseMode.Stream,   // InvokeMode.RESPONSE_STREAM and HARDENED_LAMBDA_RESPONSE_MODE=stream
});
```

`HttpApiFunctionCreate` refuses `ResponseMode = Stream`: an HTTP API buffers every response and a
stream-mode application behind it is broken rather than degraded. The function URL defaults to
`AWS_IAM` authentication, which is what a CloudFront origin access control signs for; set
`AuthType = FunctionUrlAuthType.NONE` for an application that fronts browsers directly and does its
own authentication. API Gateway REST APIs also stream since November 2025, but they send payload
format 1.0, which this host does not read yet.

The same setting applies to a function application. Under `stream` the response stream opens at
the first byte with no prelude, which is what an `InvokeWithResponseStream` caller reads; a
handler that writes nothing leaves no stream open and the invocation completes with an empty
response.

## Direct invoke

```csharp
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[LambdaFunctionModule]
public partial class Application { }

public class OrderHandler {
    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) =>
        new() { OrderId = Guid.NewGuid().ToString() };
}
```

The string in `[HardenedFunction]` is the Lambda function name the handler answers to; an
application can carry several. Omit it when the application has one handler, as the SQS and DDB
samples do.

## SQS batch

```csharp
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Sqs.Runtime;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[LambdaFunctionModule]
[SqsLambda]
public partial class Application { }

public class SqsFunctionHandler {
    [HardenedFunction]
    public Task Process(DataModel model) => Task.CompletedTask;
}
```

The handler takes the deserialised message body. A record that throws is reported in
`BatchItemFailures` by `MessageId` and redelivered; the rest of the batch still succeeds. Assert
partial failures **by identifier, not by count**. A right count against the wrong identifiers
redelivers every message and deletes the poison one.

## DynamoDB Streams

```csharp
using Hardened.Amz.Function.DDB.Runtime;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[LambdaFunctionModule]
[DynamoStreamLambda]
public partial class Application { }

public class StreamHandler {
    [HardenedFunction]
    public Task ProcessRecord(
        [OldImage] Dictionary<string, DynamoDBEvent.AttributeValue> oldImage,
        [NewImage] Dictionary<string, DynamoDBEvent.AttributeValue> newImage) => Task.CompletedTask;
}
```

`[OldImage]` and `[NewImage]` bind the two halves of a stream record. Both come from
`Hardened.Amz.Function.DDB.Runtime.Attributes`.

## Project shape

An application project is an executable. The generated `Main` runs the AWS bootstrap, and the
managed `dotnet8` runtime starts it when the function's handler names the assembly alone, which is
what `Hardened.Amz.Cdk` sets. It references the runtimes it uses, the framework generators as
packages, and the matching Amz generator as an analyzer:

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="Hardened.Library.SourceGenerator" />
    <PackageReference Include="Hardened.Function.SourceGenerator" PrivateAssets="all" />
    <PackageReference Include="Hardened.Requests.Runtime" />
</ItemGroup>

<ItemGroup>
    <ProjectReference Include="..\..\Lambda\Function\Hardened.Amz.Function.Lambda.Runtime\Hardened.Amz.Function.Lambda.Runtime.csproj" />
    <ProjectReference Include="..\..\Lambda\Shared\Hardened.Amz.Shared.Lambda.Runtime\Hardened.Amz.Shared.Lambda.Runtime.csproj" />
    <ProjectReference Include="..\..\SourceGenerators\Lambda\Function\Hardened.Amz.Function.Lambda.SourceGenerator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Web projects swap the function runtime and generator for
`Hardened.Amz.Web.Lambda.Runtime` and `Hardened.Amz.Web.Lambda.SourceGenerator`, and reference
`Hardened.Web.Runtime` and `Hardened.Web.SourceGenerator` instead of `Hardened.Function.SourceGenerator`.

Versions are not written here. Central package management is on, and every version lives in
`src/Directory.Packages.props`.

## Testing

Function-path applications — direct invoke, SQS, DDB — are tested through a harness resolved as a
test method parameter. Two assembly attributes turn it on:

```csharp
[assembly: HardenedTestEntryPoint(typeof(Application))]
[assembly: LambdaFunctionTesting]
```

`[LambdaFunctionTesting]` registers the invoke filter provider and, at startup, puts the invoke
filter into the middleware chain. **Without it nothing throws.** `MiddlewareService` holds no
filters, the execution chain is empty, the handler never runs, and the invocation returns an empty
stream, so a test that asserts only "no exception" passes against an application that did nothing.

Then take the harness and your own services as parameters:

```csharp
public class SqsFunctionHandlerTests {
    [HardenedTest]
    public async Task SingleSend(TestSqsApp app, CountingService countingService) {
        var response = await app.SendMessage(new DataModel { Value = "Hello World" });

        Assert.Empty(response.BatchItemFailures);
        Assert.Equal(1, countingService.Count);
    }
}
```

| Flavour | Harness | From |
|---|---|---|
| Direct invoke | `LambdaTestApp` | `Hardened.Amz.Function.Lambda.Testing` |
| SQS | `TestSqsApp` | `Hardened.Amz.Function.Sqs.Testing` |
| DynamoDB Streams | `TestDynamoDbStream` | `Hardened.Amz.Function.DDB.Testing` |

Web applications do not use this. They are driven directly, because the generated class is the
handler:

```csharp
private static readonly Application _application = new();

var response = await _application.Invoke(
    Request("/api/products/1"), TestLambdaContext.FromName("MyApp"));

Assert.Equal(200, response.StatusCode);
```

`TestLambdaContext` comes from `Hardened.Amz.Shared.Lambda.Testing`.

Conventions for what to assert are in [testing-conventions.md](testing-conventions.md).

## Building

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false
dotnet test  Hardened.Amz.sln -p:UseLocalHardenedFramework=false
```

`src/Directory.Build.targets` prefers a sibling `../Hardened.Framework` checkout when one exists. A
checkout at an incompatible commit fails the build with errors in *the other repository's* files.
Pass `-p:UseLocalHardenedFramework=false` to build against the pinned packages, which is what CI
does.

Before opening a pull request, run a build with the gate CI applies:

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false -p:ContinuousIntegrationBuild=true
```

That turns warnings into errors. Local builds do not.

The DynamoDB client tests need a running Docker daemon. They fail rather than skip without one.

## Failures

**`'X' is missing [SomeModule]`**
The transport module attribute is not on the application class. The generated bootstrap resolves
the transport's services in the constructor, so this fires before any request is served — and on a
deployed function, on its first invocation. The message names the attribute to add:

```
'OrderApp' is missing [LambdaWebModule]. The generated bootstrap resolves
Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService, which [LambdaWebModule] registers, so
the application cannot be constructed without it. Add [LambdaWebModule] to the class that carries
[HardenedModule].
```

Before 2026-08-27 this was a bare `No service for type '...IWebExecutionHandlerService' has been
registered`, which named a framework internal and not the attribute. If you see that older form,
the packages predate the change.

**The handler never runs and nothing throws**
`[assembly: LambdaFunctionTesting]` is missing from the test project.

**`The type or namespace name 'StreamingLambdaWebModule' could not be found`**, or the function
equivalent. The streaming modules and their packages were retired on 2026-09-04. Use
`[LambdaWebModule]` or `[LambdaFunctionModule]` and deploy with `HARDENED_LAMBDA_RESPONSE_MODE`
set to `stream`; see [Response mode](#response-mode).

**Every response is a 500 behind a function URL, or the body is missing**
The response mode and the URL's invoke mode disagree. `stream` needs `RESPONSE_STREAM`, and
`buffered` needs `BUFFERED`. `FunctionUrlFunctionCreate` sets both from one request.

**`HARDENED_LAMBDA_RESPONSE_MODE is '...'. It must be 'buffered' or 'stream'`**
The variable is set to something else. It fails the application at startup rather than falling
back to buffered behind a front door that expects the prelude.

**Errors in files under `Hardened.Framework/`**
The sibling-checkout build. Pass `-p:UseLocalHardenedFramework=false`.
