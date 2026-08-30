# API Gateway

`[LambdaWebModule]` runs the routes you already wrote behind API Gateway. The controllers, filters
and binding are the same as [any web application](/guide/routing) — only the module attribute
changes.

**Source:** [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web) in
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## An application

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

`[LambdaWebModule]` brings the API Gateway host and, through the `[HardenedWebModule]` it carries,
the web pipeline underneath it. It is not optional: an application without it compiles and then
fails at construction, naming the missing attribute.

The payload format is API Gateway **HTTP API, version 2.0**. `[LambdaWebApplication]` can state that
explicitly, and `ProxyIntegrationType.ApiGateway` — REST API, payload format 1.0 — is a build error,
`HRDAWS001`:

```csharp
[HardenedModule]
[LambdaWebModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]   // the default; optional
public partial class Application { }
```

## Configuration

Response headers are a [configuration model](/guide/configuration) the web runtime defines, and an
application amends it:

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

A Lambda web application has no HTTP server. The harness package wraps it in one: a small ASP.NET
Core host that converts each incoming request into an API Gateway event, invokes the handler, and
writes the proxy response back out.

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

The request goes through the same event conversion, routing and proxy response serialisation that
API Gateway drives, so base64 bodies, header casing and status mapping behave as they will once
deployed.

## Response streaming

For responses that should start arriving before they are complete — a long report, a token stream —
there is a streaming runtime:

```csharp
using Hardened.Amz.Web.Lambda.Streaming;

[HardenedModule]
[StreamingLambdaWebModule]
public partial class Application { }
```

It writes the Lambda response prelude and then streams the body, which needs the function deployed
with the `RESPONSE_STREAM` invoke mode. A buffered function with a streaming runtime returns the
prelude as part of the body, so this has to match the deployment. `Hardened.Amz.Cdk` does not
configure the invoke mode for you today.

## Testing

Routes are ordinary Hardened routes, so [the web test client](/guide/testing-web) drives them
without any Lambda involvement:

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

That covers routing, binding, filters and serialisation. It does not cover the API Gateway event
conversion — for that, invoke the function with a real proxy event through
[`LambdaTestApp`](/aws/testing).
