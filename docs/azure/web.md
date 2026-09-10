# Web applications

The routes you wrote for Kestrel run behind one HTTP trigger:

```csharp
using Hardened.Web.Runtime.Attributes;

public class OrderRoutes {

    [Get("/orders/{id}")]
    public Task<Order> Get(string id, IOrderStore store) => store.Get(id);

    [Post("/orders")]
    public Task<Order> Place(NewOrder order, IOrderStore store) => store.Place(order);
}
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.33.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.Http" Version="0.33.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.33.0-rc1000" PrivateAssets="all" />
```

`dotnet new hardened-web --host azure-functions` writes this shape: a library with the routes
and their tests, and a host project the Functions host starts. The library is the same one the
`aspnet` and `cloud-run` hosts build, and the template verifier proves it, file for file.

## The function

Every route is served by one function, named `Http`, bound by
`[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "put", "patch", "delete", "head", "options", Route = "{*path}")]`.
The host matches the catch-all and hands over the path it matched, and Hardened's routing table
does the routing, so `GET /orders/{id}` is dispatched exactly as it is on Kestrel. Anonymous,
because authorization is the application's, as it is on every other host; a function app that
wants the host's keys in front of it puts them in front with API Management or a front door.

The route prefix is the host's. It serves every function under `api` unless `host.json` says
otherwise, and the adapter takes the prefix off through the catch-all's value, so a route
registered as `/orders/{id}` answers at `/api/orders/{id}` by default and at `/orders/{id}` with:

```json
{
  "version": "2.0",
  "extensions": {
    "http": {
      "routePrefix": ""
    }
  }
}
```

The template writes that `host.json`, so a route answers at the same path on every host.

## The host project

The routes live in the library, the generator runs in the host project, and a generator sees
only the compilation it runs in. The host project therefore names the adapter, the way a Lambda
host writes `[ApiGatewayModule]`:

```csharp
[HardenedModule]
[HttpModule]
public partial class Application;
```

That is what makes the generator write the `Http` function into a project with no routes of its
own. A single-project application with its routes beside the entry point does not write it. The
entry point is the worker host:

```csharp
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker => worker.UseHardened<Application>())
    .Build();

host.Run();
```

`ConfigureFunctionsWorkerDefaults`, never `ConfigureFunctionsWebApplication`: the second starts
an ASP.NET Core server in the worker and hands the function `HttpRequest`; this line binds
`HttpRequestData`, which arrives over the worker channel, and nothing on the path references
ASP.NET Core.

## What the handler sees

A request: the method, the path with the prefix taken off, the query string, the headers, the
cookies and the body as the stream the host filled. The response goes back as the status, the
headers, the cookies and the buffer the pipeline wrote, without a copy. A thrown exception is
answered with the application's 500 rather than the host's, because a caller is waiting on the
connection and the host's text is not what the application chose.

## Deploying

```bash
az functionapp create --name orders --resource-group orders --storage-account ordersstorage \
    --consumption-plan-location eastus --runtime dotnet-isolated --functions-version 4
func azure functionapp publish orders
curl https://orders.azurewebsites.net/orders/A-1
```

Locally, `func start` in the host project's directory serves the same routes on port 7071.

## Testing

The routes' tests are the library's, under `[assembly: WebTesting]`, and they name no host. The
host project's test assembly adds `[assembly: AzureFunctionsWebTesting]`, which sends the same
tests' requests as the `HttpRequestData` the worker would bind, through the real invocation
handler and the adapter, so the prefix handling, the headers and the cookies are exercised too.
No test method changes. See [Testing Azure handlers](/azure/testing).

## Next

- [Queues](/azure/queue): a function app serves routes and queue handlers together
- [Testing Azure handlers](/azure/testing): the three rungs
- [Application types](https://github.com/ipjohnson/Hardened.Framework/blob/main/docs/design/azure/application-types.md): the design in full
