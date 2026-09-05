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
it locally over HTTP. Source: [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

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

A Lambda web application has no HTTP server. The harness package wraps it in one: a small
ASP.NET Core host that converts each incoming request into an API Gateway event, invokes the
handler, and writes the proxy response back out.

```csharp
using Hardened.Amz.Web.Lambda.Harness;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLambdaApplication<Application>();

var app = builder.Build();

app.UseLambdaApplication();

app.Run();
```

Keep this in its own project, `MyApi.Harness` beside `MyApi`, so the deployed artefact does not
carry a web server it will never start. The request goes through the same event conversion,
routing and proxy response serialization that API Gateway drives, so base64 bodies, header casing
and status mapping behave as they will once deployed.

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
