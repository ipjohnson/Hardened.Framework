# API Gateway

`[LambdaWebModule]` runs the routes you already wrote behind API Gateway. The controllers, filters
and binding are the same as [any web application](/guide/routing). Only the module attribute
changes.

```csharp
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[LambdaWebModule]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

```csharp
[HardenedTest]
public async Task GetsAProduct(ITestWebApp testWebApp) {
    var response = await testWebApp.Get("/api/products/42");

    response.Assert.Ok();
    Assert.Equal("42", response.Deserialize<Product>().Id);
}
```

`dotnet new hardened-web --host aws-lambda` writes this shape, with a harness project that runs
it locally over HTTP. Source: [`src/Clouds/Aws/Lambda/Web`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Lambda/Web)
in [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework).

## The module

`[LambdaWebModule]` brings the API Gateway host and, through the `[HardenedWebModule]` it
carries, the web pipeline underneath it. It is not optional: an application without it compiles
and then fails at construction, naming the missing attribute.

The payload format is API Gateway HTTP API, version 2.0. `[LambdaWebApplication]` can state that
explicitly. `ProxyIntegrationType.ApiGateway`, the REST API's payload format 1.0, is build error
`HRDAWS001`:

```csharp
[HardenedModule]
[LambdaWebModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]   // the default; optional
public partial class Application { }
```

## Configuration

Response headers are a [configuration model](/guide/configuration) the web runtime defines, and
an application amends it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Configuration;

[HardenedModule]
[LambdaWebModule]
public partial class Application : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        var config = new AppConfig();

        config.Amend((ResponseHeaderConfiguration response) =>
            response.Add("Access-Control-Allow-Origin", "*"));

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

## Running it locally

Run the application project, from the IDE or with `dotnet run`:

```bash
dotnet run --project src/MyApi
Started the AWS Lambda Test Tool on http://localhost:5050
Listening on http://localhost:5080
```

```bash
curl localhost:5080/api/products/42
```

A Lambda web application has no HTTP server of its own, and nothing starts it locally the way the
Lambda service does. So when `AWS_LAMBDA_RUNTIME_API` is unset, the generated `Main` starts the
[AWS Lambda Test Tool](https://github.com/aws/aws-lambda-dotnet/tree/master/Tools/LambdaTestTool-v2)
as a child process and points its bootstrap at it. The tool's API Gateway emulator takes HTTP on
5080, turns each request into a payload format 2.0 event, and hands the function's response back
as HTTP. The debugger is on the process the Lambda service would start, running the same `Main`,
bootstrap and event serialization.

| Variable | Default | |
|---|---|---|
| `PORT` | `5080` | The API Gateway emulator, where the application answers |
| `HARDENED_LAMBDA_EMULATOR_PORT` | `5050` | The Lambda Runtime API emulator and the tool's page |
| `AWS_LAMBDA_RUNTIME_API` | unset | Set by Lambda and by the runtime interface emulator. When set, none of this runs |

The tool is a dotnet tool. The [templates](/guide/project-templates) pin it in a tool manifest and
restore it in the build. Elsewhere, `dotnet tool install -g amazon.lambda.testtool`. A tool left
running by the debugger's stop button is found on its port and reused by the next start.

The gateway emulator buffers, so a stream-mode application is exercised by its tests and by a
deployment; see [Response mode](#response-mode).

## Response mode

Every response leaves the function in one of two ways, and the deployment decides which:

| `HARDENED_LAMBDA_RESPONSE_MODE` | The function sends | Front doors that accept it |
|---|---|---|
| `buffered` (default) | The payload format 2.0 JSON, when the handler returns | API Gateway HTTP API, or a function URL in `BUFFERED` invoke mode |
| `stream` | A prelude of status, headers and cookies, then the body as it is produced | A function URL in `RESPONSE_STREAM` invoke mode, with or without CloudFront in front |

Reach for `stream` when a response should start arriving before it is complete: a long report, a
token stream, anything returning `IAsyncEnumerable<T>`.

This is a deployment setting rather than an attribute because the front doors are strict and the
function cannot tell them apart from the event. A `RESPONSE_STREAM` URL answers a plain payload
with a 500, and a buffered front door drops the body of a streamed response. The variable is
read once at startup, and an unrecognised value fails the application there. An application can
also set it in code:

```csharp
private void Configure(IAppConfig config) {
    config.Amend((LambdaResponseModeConfiguration mode) => mode.Mode = LambdaResponseMode.Stream);
}
```

Under `stream` the pipeline does not change. The response body opens the Lambda response stream
at its first byte, with whatever status and headers the pipeline had decided by then. A buffered
operation is one write and a close. A handler returning `IAsyncEnumerable<T>` is a write per
item, flushed as each item is produced. A refusal opens the stream with the refusal's status and
a JSON body, so an `EventSource` stops rather than reconnecting forever.

Errors follow the same rule. Before the first byte the pipeline serializes the error as usual and
that byte opens the stream with the error's status. After the first byte the exception is
written as trailers and the invocation is recorded as failed, and the client sees a truncated
stream.

An application with `[ServerSentEvents]` handlers deployed in buffered mode logs a warning at
startup naming them. The build cannot refuse the combination, because the build does not know the
deployment.

### Deploying it

`Hardened.Amz.Cdk` writes the variable and the invoke mode from one setting, so the two cannot
disagree:

```csharp
var (function, url) = lambdaCdkUtil.FunctionUrlFunctionCreate(new FunctionUrlLambdaRequest {
    Name = "orders",
    ApplicationType = typeof(Application),
    ResponseMode = LambdaResponseMode.Stream,
});
```

`HttpApiFunctionCreate` refuses `ResponseMode = Stream`. An HTTP API buffers every response, so a
stream-mode application behind one is broken rather than degraded.

The function URL defaults to `AWS_IAM` authentication, which is what a CloudFront origin access
control signs for. Set `AuthType = FunctionUrlAuthType.NONE` for an application that fronts
browsers directly and does its own authentication.

## Testing

Routes are ordinary Hardened routes, so [`ITestWebApp`](/guide/testing-web) drives them without
any Lambda involvement, as the test at the top shows:

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

That covers routing, binding, filters and serialization. It does not cover the API Gateway event
conversion. For that, invoke the function with a real proxy event through
[`LambdaTestApp`](/aws/testing).

## Next

- [Streaming responses](/guide/streaming#where-it-works): what streams under each mode
- [CDK](/aws/cdk): the deployment application
- [Testing AWS handlers](/aws/testing): the Lambda harnesses
