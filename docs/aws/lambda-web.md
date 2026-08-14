# API Gateway

`[LambdaWebApplication]` runs the routes you already wrote behind API Gateway. The controllers,
filters and binding are the same as [any web application](/guide/routing) — only the module attribute
changes.

**Source:** [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web) in
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## An application

```csharp
using Hardened.Amz.Web.Lambda.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

`Version` picks the payload format:

| Value | Event |
|---|---|
| `ProxyIntegrationType.HttpApiV2` | HTTP API, payload format version 2.0 |
| `ProxyIntegrationType.ApiGateway` | REST API / HTTP API payload format 1.0 |

The two formats differ in how the method, path and query string are carried, and mixing them up
produces a Lambda that returns 404 for every route rather than an error you can read. Match this to
the integration you configured in the gateway.

## Configuration

The module is a module, so the usual hooks apply. Response headers are a
[configuration model](/guide/configuration) the web runtime defines, and an application amends it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Configuration;

[HardenedModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]
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

A Lambda web application has no HTTP server, which makes it awkward to point a browser or a
front-end dev server at. The harness package wraps it in one: a small ASP.NET Core host that converts
each incoming request into an API Gateway event, invokes the handler, and writes the proxy response
back out.

```csharp
using Hardened.Amz.Web.Lambda.Harness;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLambdaApplication<Application>();

var app = builder.Build();

app.UseLambdaApplication();

app.Run();
```

Keep this in its own project — `MyApi.Harness` beside `MyApi` — so the deployed artefact does not
carry a web server it will never start.

::: tip This is the closest local approximation of production
The request goes through the same event conversion, the same routing and the same proxy response
serialisation that API Gateway will drive. Base64 bodies, header casing and status mapping all behave
as they will once deployed, which is where local-versus-deployed differences usually hide.
:::

## Response streaming

For responses that should start arriving before they are complete — a long report, a token stream —
there is a streaming runtime:

```csharp
using Hardened.Amz.Web.Lambda.Streaming;

[HardenedModule]
[StreamingLambdaWebApplication]
public partial class Application { }
```

It writes the Lambda response prelude and then streams the body, which requires the function to be
configured with `RESPONSE_STREAM` invoke mode. A buffered function with a streaming runtime returns
the prelude as part of the body, so this has to match the deployment.

## Testing

Routes are ordinary Hardened routes, so [the web test client](/guide/testing-web) drives them without
any Lambda involvement:

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

```csharp
[HardenedTest]
public async Task GetsAProduct(ITestWebApp testWebApp) {
    var response = await testWebApp.Get("/api/products/42");

    response.Assert.Ok();
    Assert.Equal("42", response.Deserialize<Product>().Id);
}
```

That covers routing, binding, filters and serialisation. What it does not cover is the API Gateway
event conversion — for that, invoke the function with a real proxy event through
[`LambdaTestApp`](/aws/testing).
