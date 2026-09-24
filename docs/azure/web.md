# Web applications

`[HttpModule]` serves an application's routes from Azure Functions, through one HTTP function named
`Http`. The routes, filters and parameter binding are the ones that the application has on every
host. [Routing](/guide/routing) covers them.

`dotnet new hardened-web -n Todos --host azure-functions` writes a library with the routes, a host
project for Azure Functions, a client project and a test project. The application class in
`src/Todos.Host/Application.cs` names `[HttpModule]`. With `--openapi-ui false`, and without its
comments and an unused `using` line, the class is this:

```csharp
using Hardened.Azure.Functions.Http;
using Hardened.Shared.Runtime.Attributes;

namespace Todos.Host;

[HardenedModule]
[HttpModule]
[TodosLibrary]
public partial class Application;
```

After `func start` in `src/Todos.Host`, the template's route answers:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

The Functions host receives each request and hands it to the worker. The adapter turns the worker's
`HttpRequestData` into the request that the routing table matches. It turns the response into the
worker's `HttpResponseData`.

[Hosts](/guide/hosts) covers the `Http` function, the host project's `Program.cs`,
`local.settings.json`, and running the application with `func start` on port 7071. The Azure
[Overview](/azure/) covers the storage account that the Functions host uses locally.

## Packages and the application class

The host project references four Azure packages. The template keeps their versions in
`Directory.Packages.props`. Without central package management, the references are these:

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.Http" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
```

The Azure [Overview](/azure/) lists the other Azure packages.

`Hardened.Azure.Functions.Http` sets the build property `HardenedHttpModule` to
`Hardened.Azure.Functions.Http.HttpModule`. When a project compiles its routes together with its
application class, the build registers `HttpModule` from that property and writes the `Http`
function. The application class then names no Azure attribute.

When the routes are in a referenced library, as in the template, the application class names
`[HttpModule]` itself. The host project still builds without the attribute. Under `func start`, the
Functions host finds no function:

```text
No job functions found. Try making your job classes and methods public. If you're using binding extensions (e.g. Azure Storage, ServiceBus, Timers, etc.) make sure you've called the registration method for the extension(s) in your startup code (e.g. builder.AddAzureStorage(), builder.AddServiceBus(), builder.AddTimers(), etc.).
```

Every request then answers 404.

`[HttpModule]` has no settings. It does not bring `[HardenedWebModule]`. The module that holds the
routes declares that attribute, as the template's `TodosLibrary` does. [Hosts](/guide/hosts) covers
the attribute.

## The route prefix

The Functions host serves HTTP functions under the path prefix `api`, unless `host.json` sets
`extensions.http.routePrefix`. The template's `src/Todos.Host/host.json` sets the prefix to an empty
string:

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      },
      "enableLiveMetricsFilters": true
    }
  },
  "extensions": {
    "http": {
      "routePrefix": ""
    }
  }
}
```

Each route then answers at its own path, as on every other host. [Hosts](/guide/hosts) covers
`host.json`.

The adapter routes on the path that the host matched under `{*path}`. The routing table never sees
the prefix. Without the `extensions` section, the host uses its default prefix. The routes then
answer under `/api`. `GET /api/todos/1` reaches the route `GET /todos/1`:

```http
GET /api/todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

A request outside the prefix never reaches the application. The host answers it with 404 and no
body.

The paths that the application writes, and the links that it builds, do not carry the prefix.
`POST /api/todos` answers with `Location: /todos/3`:

```http
POST /api/todos
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Write the docs","done":false}
```

[Route links](/guide/route-links) covers giving the links a base path.

## What a handler receives

| The request's | Comes from the worker's `HttpRequestData` |
|---|---|
| Method | `Method` |
| Path | The path that the host matched under `{*path}`, with `/` in front. The route prefix is not part of it |
| Query string | `Query`, which the worker has already decoded |
| Headers | `Headers` |
| Cookies | `Cookies`, as `name=value` strings |
| Body | `Body`, the stream that the host filled |

The Azure [Overview](/azure/) covers the `CancellationToken` that a handler receives.

## What the function answers

| The response's | Goes to the caller as |
|---|---|
| Status | The status code. 200 when the handler set none |
| Headers | Headers. A header with several values goes as one line, its values joined with commas |
| Cookies | One `Set-Cookie` header for each cookie. The host adds `path=/` to a cookie that names no path |
| Body | The bytes that the pipeline wrote, unchanged |

A `byte[]` that a handler returns reaches the caller as written. The adapter does not read the
response's `IsBinary`.

## Failures

| Request | Answer |
|---|---|
| The handler throws | 500 with the error body. The host logs the invocation as succeeded |
| No route matches the path | 404 with no body |
| A validation constraint fails | 400 with the error body |
| The path is outside the route prefix | 404 with no body, from the host |

[Declared responses](/guide/responses) covers the error body.

The Azure [Overview](/azure/) covers what a trigger's source does after a failed invocation.

## Startup services

The worker runs the application's startup services when it starts, before it connects to the
Functions host. Authentication, authorization, CORS and the routes registered at startup then apply
as on the other hosts.

With `[RequireAuthorization]` on the application class in `src/Todos.Host/Application.cs`, a request
with no credentials gets 401:

```csharp
using Hardened.Azure.Functions.Http;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Shared.Runtime.Attributes;

namespace Todos.Host;

[HardenedModule]
[HttpModule]
[RequireAuthorization]
[TodosLibrary]
public partial class Application;
```

```http
GET /todos

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

The Azure [Overview](/azure/) covers the worker. [Authentication](/guide/authentication),
[Authorization](/guide/authorization), [CORS](/guide/cors),
[Registered routes](/guide/registered-routes) and [The execution pipeline](/guide/execution-pipeline)
cover each feature on the other hosts.

## Trigger handlers in the same function app

A web function app can also serve trigger handlers. The host project references the trigger's
adapter package and `Hardened.Function.SourceGenerator`. For a timer, these lines go in
`src/Todos.Host/Todos.Host.csproj`:

```xml
<PackageReference Include="Hardened.Azure.Functions.Timer" />
<PackageReference Include="Hardened.Function.SourceGenerator" PrivateAssets="all" />
```

The web template's `Directory.Packages.props` pins neither package. Without a `PackageVersion` line
for each, the restore fails with `NU1010`. These lines go in `Directory.Packages.props`:

```xml
<PackageVersion Include="Hardened.Azure.Functions.Timer" Version="$(HardenedVersion)" />
<PackageVersion Include="Hardened.Function.SourceGenerator" Version="$(HardenedVersion)" />
```

The handler is in the host project, as in `src/Todos.Host/Housekeeping.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Todos.Host;

public class Housekeeping
{
    [Timer("nightly")]
    public void Nightly() => Console.WriteLine("Nightly housekeeping ran");
}
```

The timer's schedule is the app setting `Hardened:Timers:nightly`. Locally, the setting goes in
`local.settings.json`. Without it, the Functions host disables the timer function. The routes still
answer.
[Timers](/azure/timer) covers the setting.

The Azure [Overview](/azure/) covers the trigger functions, running them locally through the host's
admin endpoint, and what a failed trigger does.

## Deploying

The Azure [Overview](/azure/) covers creating the function app and `func azure functionapp publish`.

The `Http` function's authorization level is `Anonymous`, so a deployed route asks for no function
key. [Hosts](/guide/hosts) covers the level.

The deployed function app reads the template's `host.json`, so its routes answer at their own paths.

## Testing

[Testing](/azure/testing) covers testing a web application on Azure Functions.

## Limits

Azure's load balancer answers 502 to a request whose function has not responded within 230 seconds.
The function keeps running.

The Functions host keeps the paths that start with `/admin` and `/runtime` for its own APIs. With the
template's empty prefix, a route under either path does not reach the application in Azure.

`func start` answers the host's own paths, such as `POST /admin/functions/<name>`. It passes
`GET /admin/stats` and `GET /runtime/info` to the application's routes for those paths, so a local
run does not show the limit.

[Azure Functions HTTP trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-http-webhook-trigger)
on Microsoft Learn covers both limits.

## Next

| Page | Covers |
|---|---|
| [Overview](/azure/) | The Azure packages, the worker's entry point, and deploying a function app |
| [Hosts](/guide/hosts) | The `Http` function, the host project's `Program.cs` and `func start` |
| [Testing](/azure/testing) | Testing on Azure |
| [Authorization](/guide/authorization) | The authorization attributes that the warning is about |
| [Streaming responses](/guide/streaming) | Streaming a response, and which hosts stream |
