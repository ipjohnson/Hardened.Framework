# CORS

Every application that imports `[HardenedWebModule]` runs `CorsFilter` on every request. The filter
answers CORS preflights and adds the CORS headers to responses for an allowed origin.

No origin is allowed until the application allows one. The environment variable
`CORS_ALLOWED_ORIGINS` lists the allowed origins, separated by commas. This command, run from the
solution directory, starts the application with one allowed origin:

```bash
CORS_ALLOWED_ORIGINS=https://app.example.com dotnet run --project src/Todos.Host
```

A request from that origin gets the CORS headers:

```http
GET /todos/1
Origin: https://app.example.com

HTTP/1.1 200 OK
Content-Type: application/json
Access-Control-Allow-Origin: https://app.example.com
Access-Control-Expose-Headers: X-Correlation-Id
Vary: Origin

{"id":1,"title":"Read the generated code","done":true}
```

## The notice at startup

An application that allows no origin logs a notice at startup. The notice has the level
`Information` and the category `Hardened.Web.Runtime.Cors.CorsStartupService`. The `hardened-web`
template allows no origin, so a new application logs the notice on its first run:

```console
$ dotnet run --project src/Todos.Host
info: Hardened.Web.Runtime.Cors.CorsStartupService[0] CORS is registered with no allowed origins, so every cross-origin request will be refused. Set CORS_ALLOWED_ORIGINS or call AllowOrigin to configure it.
info: Todos.Host[0] Listening on http://localhost:5080
```

The filter is installed whether or not an origin is allowed. With no origin allowed, it refuses every
cross-origin request.

The notice is not logged when `CORS_ALLOWED_ORIGINS` or a configuration registered in
`ConfigureServices` allows an origin. A startup service that calls `AllowOrigin` on the registered
configuration allows the origin for requests. The notice is still logged.

A logging filter at `Warning` for the category hides the notice. This `AddLogging` call in
`src/Todos.Host/Program.cs` adds one:

```csharp
using Microsoft.Extensions.Logging;

services.AddLogging(logging => logging
    .AddSimpleConsole(options => options.SingleLine = true)
    .AddFilter("Hardened.Web.Runtime.Cors.CorsStartupService", LogLevel.Warning));
```

## Allow origins with `CORS_ALLOWED_ORIGINS`

Each entry in the variable takes one of three forms:

| Entry | Allows | Refuses |
|---|---|---|
| `https://partner.test` | That origin, in any letter case, with or without a trailing `/` | `http://partner.test` and `https://partner.test:8443` |
| `*.example.com` | Every subdomain of `example.com`, at any depth, with any scheme and any port: `https://app.example.com`, `https://eu.app.example.com`, `http://app.example.com`, `https://app.example.com:8443` | `https://example.com`, `https://notexample.com` |
| `*` | Every origin | Nothing |

`Access-Control-Allow-Origin` repeats the origin as the request sent it. With `*`, the responses
carry `Access-Control-Allow-Origin: *`:

```http
GET /todos/1
Origin: https://anything.test

HTTP/1.1 200 OK
Content-Type: application/json
Access-Control-Allow-Origin: *
Access-Control-Expose-Headers: X-Correlation-Id
Vary: Origin

{"id":1,"title":"Read the generated code","done":true}
```

Spaces around an entry are ignored. Empty entries are skipped. A variable that is unset, blank or
holds only commas allows nothing. The variable is read once, when the configuration is built at
startup.

## Allow origins in code

`CorsConfiguration`, in `Hardened.Web.Runtime.Cors`, holds the CORS settings. The web module builds
a configuration that reads `CORS_ALLOWED_ORIGINS`. A configuration that the application registers in
`ConfigureServices` replaces it.

::: warning
A configuration registered in `ConfigureServices` ignores `CORS_ALLOWED_ORIGINS` unless the
application calls `LoadFromEnvironment()` on it. Without the call, the origins in the variable are
refused. Nothing is logged when the code allows an origin of its own.
:::

`src/Todos.Host/ApplicationCors.cs` registers a configuration on the host project's application
module:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Web.Runtime.Cors;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var cors = new CorsConfiguration { AllowCredentials = true };

        cors.LoadFromEnvironment();
        cors.AllowOrigin("https://app.example.com");
        cors.AllowOriginSuffix("example.org");
        cors.AllowHeader("X-Request-Id");
        cors.ExposeHeader("Location");

        services.AddSingleton(cors);
    }
}
```

With that file, a preflight and a request from `https://shop.example.org`, a subdomain of
`example.org`, get these answers:

```http
OPTIONS /todos
Origin: https://shop.example.org
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type, x-request-id

HTTP/1.1 204 No Content
Access-Control-Allow-Credentials: true
Access-Control-Allow-Headers: content-type, x-request-id
Access-Control-Allow-Methods: POST
Access-Control-Allow-Origin: https://shop.example.org
Access-Control-Max-Age: 86400
Vary: Origin
```

```http
POST /todos
Origin: https://shop.example.org
Content-Type: application/json
X-Request-Id: 7

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Access-Control-Allow-Credentials: true
Access-Control-Allow-Origin: https://shop.example.org
Access-Control-Expose-Headers: X-Correlation-Id, Location
Location: /todos/3
Vary: Origin

{"id":3,"title":"Write the docs","done":false}
```

The configuration has these members:

| Member | Default | Does |
|---|---|---|
| `AllowOrigin(origin)` | | Allows one origin, as an exact entry in `CORS_ALLOWED_ORIGINS` does |
| `AllowOriginSuffix(domain)` | | Allows every subdomain of `domain`, as `*.domain` does |
| `AllowAnyOrigin` | `false` | Allows every origin, as `*` does |
| `AllowCredentials` | `false` | Sends `Access-Control-Allow-Credentials: true` |
| `AllowHeader(header)` | | Adds a request header a preflight may name |
| `ExposeHeader(header)` | | Adds a response header a script may read |
| `ClearExposedHeaders()` | | Removes every exposed header, `X-Correlation-Id` included |
| `MaxAgeSec` | `86400` | How long a browser may keep a preflight's answer, in seconds. Sent as `Access-Control-Max-Age` |
| `FallbackMethods` | `GET, POST, PUT, DELETE, OPTIONS` | The methods a preflight for a path with no route is told |
| `EnvironmentVariable` | `CORS_ALLOWED_ORIGINS` | The variable `LoadFromEnvironment()` reads |
| `LoadFromEnvironment()` | | Adds the entries of the variable, in the forms that `CORS_ALLOWED_ORIGINS` accepts |

## Cross-origin requests

A request whose `Origin` header names an allowed origin gets `Access-Control-Allow-Origin` and
`Access-Control-Expose-Headers`. The request continues to the handler.

A request from a refused origin gets no CORS headers. The handler still runs. With
`CORS_ALLOWED_ORIGINS=https://app.example.com`, this request from another origin adds a todo:

```http
POST /todos
Origin: https://evil.example.net
Content-Type: application/json

{"title":"Posted from elsewhere"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3
Vary: Origin

{"id":3,"title":"Posted from elsewhere","done":false}
```

A request without an `Origin` header gets no CORS headers and no `Vary: Origin`.

The filter runs before routing, so a 404 or a 401 to an allowed origin carries the CORS headers.

An `OPTIONS` request without `Access-Control-Request-Method` is not a preflight.
[Routing](/guide/routing) handles it like any other request.

## Preflights

An `OPTIONS` request that carries `Access-Control-Request-Method` is a preflight. The filter answers
it with 204 and no body. A preflight never reaches routing, authorization or a handler. A preflight
for a route that requires a caller is answered like any other.

A preflight gets one of these answers:

| The preflight | Answer |
|---|---|
| From an allowed origin, for a method the path has a route for, naming allowed headers | 204 with the CORS headers |
| For a path with no route | 204 with the CORS headers. `Access-Control-Allow-Methods` is `FallbackMethods` |
| From a refused origin | 204 with no CORS headers |
| For a method the path has no route for | 204 with no CORS headers |
| Naming a header that is not allowed | 204 with no CORS headers |

With `CORS_ALLOWED_ORIGINS=https://app.example.com`, a preflight from that origin gets the CORS
headers:

```http
OPTIONS /todos
Origin: https://app.example.com
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type

HTTP/1.1 204 No Content
Access-Control-Allow-Headers: content-type
Access-Control-Allow-Methods: POST
Access-Control-Allow-Origin: https://app.example.com
Access-Control-Max-Age: 86400
Vary: Origin
```

A refused preflight is also a 204, with `Vary: Origin` and no CORS headers:

```http
OPTIONS /todos
Origin: https://evil.example.net
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type

HTTP/1.1 204 No Content
Vary: Origin
```

An allowed preflight gets `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods` and
`Access-Control-Max-Age`. It also gets `Access-Control-Allow-Headers` when it names headers.

When the path has a route for the requested method, `Access-Control-Allow-Methods` holds that method
alone. `Access-Control-Allow-Headers` repeats the requested headers as the request wrote them.

By default, a preflight may name `Authorization`, `Content-Type`, `Accept`, `x-auth-token` and
`x-amz-content-sha256`. The filter compares header names without regard to case. `AllowHeader` adds
one.

## Exposed headers

`Access-Control-Expose-Headers` lists `X-Correlation-Id` by default. Every response carries an
`X-Correlation-Id` header. `ExposeHeader` adds a header to the list. `ClearExposedHeaders` empties
the list. The filter then sends no `Access-Control-Expose-Headers`.

The filter sends the list on responses to actual requests. A preflight's answer does not carry it.

## Credentials

`AllowCredentials = true` adds `Access-Control-Allow-Credentials: true` to the answer to a preflight
and to the response to an actual request. `CORS_ALLOWED_ORIGINS` has no form for credentials. Only a
configuration in code allows them.

When `AllowAnyOrigin` is `true` as well, the filter sends the request's origin in
`Access-Control-Allow-Origin`, in place of `*`. It leaves out `Access-Control-Allow-Credentials`.

## The `Vary` header

Every response to a request with an `Origin` header carries `Vary: Origin`, whether the origin is
allowed or refused. A preflight's 204 carries it too. The filter adds `Origin` to the `Vary` values
that other filters write. For example, a compressed response carries
`Vary: Origin, Accept-Encoding`. [Compression](/guide/compression) describes when a response is
compressed.

## Limits

One configuration covers every route of the application. No attribute or setting changes CORS for
one route.

On Azure Functions, the worker never runs the startup services, so no CORS policy applies. The Azure
[Web applications](/azure/web) page covers it.

## Next

- [Authentication](/guide/authentication): the credentials a cross-origin request carries
- [The execution pipeline](/guide/execution-pipeline): the middleware chain the filter runs in
- [Modules](/guide/modules): `ConfigureServices` and startup services
