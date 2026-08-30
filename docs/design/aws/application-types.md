# Lambda application types

Every Hardened.Amz application is one entry point class carrying `[HardenedModule]` and exactly one
transport module attribute. The transport module registers the runtime services and selects which
bootstrap the source generator emits. The attribute on the class is the whole decision.

| Transport | Module attribute | Generator package |
|---|---|---|
| API Gateway (buffered) | `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.SourceGenerator` |
| API Gateway with response streaming | `[StreamingLambdaWebModule]` | `Hardened.Amz.Web.Lambda.SourceGenerator` |
| Direct invoke | `[LambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |
| Direct invoke with response streaming | `[StreamingLambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |
| SQS batch | `[LambdaFunctionModule]` + `[SqsLambda]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |
| DynamoDB Streams | `[LambdaFunctionModule]` + `[DynamoStreamLambda]` | `Hardened.Amz.Function.Lambda.SourceGenerator` |

SQS and DynamoDB Streams take a second attribute because they are event sources layered on the
direct-invoke path rather than transports of their own. `[SqsLambda]` and `[DynamoStreamLambda]` add
batch handling; `[LambdaFunctionModule]` underneath brings the invocation path and the request
pipeline.

## Five rules

1. **`[HardenedModule]` is always required.** It is what marks a class as an entry point. Both
   generators ignore a class without it, so a transport module on its own produces nothing.
2. **The attribute goes on the application class itself.** Attributes on members or nested types
   select nothing.
3. **One transport module per application.** The buffered and streaming selectors are exact
   complements, so a class gets one bootstrap or the other, never both and never neither.
4. **Reference the matching source generator as an `Analyzer`**, with
   `ReferenceOutputAssembly="false"`. Without it nothing is generated and the application has no
   `Invoke` or `Main` at all.
5. **The module is the opt-in.** Applying it registers the services *and* selects the bootstrap.
   If an application compiles but throws on construction, a module attribute is missing — see
   [Failures](#failures).

## API Gateway, buffered

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

The generated class implements `IApiGatewayV2Handler`, so it is driven with
`Application.Invoke(request, context)` taking an `APIGatewayHttpApiV2ProxyRequest`.

Payload format 2.0 only. `[LambdaWebApplication(Version = ProxyIntegrationType.ApiGateway)]`
selects REST API payload format 1.0, which is not implemented and is a build error (`HRDAWS001`).

## API Gateway with response streaming

```csharp
using Hardened.Amz.Web.Lambda.Streaming;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[StreamingLambdaWebModule]
public partial class Application { }
```

Controllers are written the same way. What differs is the host: the generator emits a `Main` that
drives the Lambda Runtime API directly and writes the
`application/vnd.awslambda.http-integration-response` format — a JSON prelude of status and
headers, then the body as it is produced. The function must be deployed with the `RESPONSE_STREAM`
invoke mode. `Hardened.Amz.Cdk` does not configure that today.

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

Add `[StreamingLambdaFunctionModule]` instead of `[LambdaFunctionModule]` for the streaming
variant — it carries `[LambdaFunctionModule]` itself, so it replaces rather than accompanies it.

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

An application project references the runtimes it uses, the framework generators as packages, and
the matching Amz generator as an analyzer:

```xml
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

**`'StreamingLambdaWebApplicationAttribute' is obsolete`**
It registered nothing, so applications using it threw on construction. Use
`[StreamingLambdaWebModule]`.

**`The type or namespace name 'StreamingLambdaFunctionAttribute' could not be found`**
Use `[StreamingLambdaFunctionModule]`. `[StreamingLambdaFunctionApplication]` and
`[LambdaFunctionApplication]` never had a type behind them and are gone.

**Errors in files under `Hardened.Framework/`**
The sibling-checkout build. Pass `-p:UseLocalHardenedFramework=false`.
