# CORS

Every application that imports `[HardenedWebModule]` runs `CorsFilter` on every request. The filter
answers CORS preflights. In an application that declares no `[Cors]`, it also adds the CORS headers
to every response for an allowed origin. `[Cors]` on a handler, its class or its module limits CORS
to the routes it covers, as [CORS on some routes](#cors-on-some-routes) describes.

No origin is allowed until the application allows one. The environment variable
`CORS_ALLOWED_ORIGINS` lists the allowed origins, separated by commas. From the solution directory,
this command starts the application with one allowed origin:

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

## The startup notice

An application that declares no `[Cors]` and allows no origin logs a notice at startup. The notice
is at `Information`, in the category `Hardened.Web.Runtime.Cors.CorsStartupService`. The template
allows no origin, so a new application logs the notice on its first run:

```console
$ dotnet run --project src/Todos.Host
info: Hardened.Web.Runtime.Cors.CorsStartupService[0] CORS is registered with no allowed origins, so every cross-origin request will be refused. Set CORS_ALLOWED_ORIGINS or call AllowOrigin to configure it.
info: Todos.Host[0] Listening on http://localhost:5080
```

With no origin allowed, the filter is still installed. It refuses every cross-origin request.

| The application | The notice |
|---|---|
| Allows an origin through `CORS_ALLOWED_ORIGINS` | Not logged |
| Allows an origin in a configuration registered in `ConfigureServices` | Not logged |
| Allows an origin from a startup service that calls `AllowOrigin` on the registered configuration | Logged. The origin is still allowed for requests |
| Declares `[Cors]` on a route or a module | Not logged, whether or not an origin is allowed |

[Allowing origins in code](#allowing-origins-in-code) shows a configuration registered in
`ConfigureServices`.

A logging filter at `Warning` for the category hides the notice. This excerpt of
`src/Todos.Host/Program.cs` replaces the template's `services.AddLogging(...)` line:

```csharp
using Microsoft.Extensions.Logging;

services.AddLogging(logging => logging
    .AddSimpleConsole(options => options.SingleLine = true)
    .AddFilter("Hardened.Web.Runtime.Cors.CorsStartupService", LogLevel.Warning));
```

## Allowing origins with `CORS_ALLOWED_ORIGINS`

An entry in `CORS_ALLOWED_ORIGINS` takes one of three forms:

| Entry | Allows | Refuses |
|---|---|---|
| `https://partner.test` | That origin, in any letter case, with or without a trailing `/` | `http://partner.test` and `https://partner.test:8443` |
| `*.example.com` | Every subdomain of `example.com`, at any depth, with any scheme and any port: `https://app.example.com`, `https://eu.app.example.com`, `http://app.example.com`, `https://app.example.com:8443` | `https://example.com`, `https://notexample.com` |
| `*` | Every origin | Nothing |

Spaces around an entry are ignored. Empty entries are skipped. A variable that is unset, blank or
holds only commas allows nothing. The variable is read once, when the configuration is built at
startup.

`Access-Control-Allow-Origin` repeats the origin as the request sent it. With
`CORS_ALLOWED_ORIGINS='*'`, the responses carry `Access-Control-Allow-Origin: *`:

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

## Allowing origins in code

`CorsConfiguration` holds the settings. It is in the `Hardened.Web.Runtime.Cors` namespace. A
configuration that the application registers in `ConfigureServices` replaces the one that the web
module builds. The file `src/Todos.Host/ApplicationCors.cs` registers one on the host project's
application module:

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

The web module's configuration is the one that reads `CORS_ALLOWED_ORIGINS`. A configuration that
the application registers reads the variable only when the application calls
`LoadFromEnvironment()`, as the file above does.

::: warning
A configuration registered in code without `LoadFromEnvironment()` refuses the origins in
`CORS_ALLOWED_ORIGINS`. Nothing is logged when the code allows an origin of its own.
:::

With that file, a preflight from `https://shop.example.org` that names `x-request-id` is allowed:

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

The response to the `POST` carries `Access-Control-Allow-Credentials: true` and exposes `Location`:

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
| `FallbackMethods` | `GET, POST, PUT, DELETE, OPTIONS` | The methods a preflight for a path with no route is told, in an application that declares no `[Cors]` |
| `EnvironmentVariable` | `CORS_ALLOWED_ORIGINS` | The variable `LoadFromEnvironment()` reads |
| `LoadFromEnvironment()` | | Adds the entries of the variable, in the three forms of the previous section |

## Cross-origin requests

[CORS on some routes](#cors-on-some-routes) covers the routes of an application that declares
`[Cors]`. In an application that declares no `[Cors]`, a request gets one of these answers:

| The request | Answer |
|---|---|
| With an `Origin` header from an allowed origin | `Access-Control-Allow-Origin` and `Access-Control-Expose-Headers`. The handler runs |
| From a refused origin | No CORS headers. The handler still runs |
| Without an `Origin` header | No CORS headers and no `Vary: Origin` |

With `CORS_ALLOWED_ORIGINS=https://app.example.com`, a request from another origin still adds a
todo:

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

The filter runs for every request, before routing. A 404 and a 401 to an allowed origin carry the
CORS headers.

An `OPTIONS` request without `Access-Control-Request-Method` is not a preflight. It is routed like
any other request, as [Routing](/guide/routing) describes.

## Preflights

An `OPTIONS` request that carries `Access-Control-Request-Method` is a preflight. The filter answers
it with 204 and no body. A preflight never reaches routing, authorization or a handler. A preflight
for a route that requires a caller is answered like any other.

In an application that declares no `[Cors]`, a preflight gets one of these answers:

| The preflight | Answer |
|---|---|
| From an allowed origin, for a method the path has a route for, naming allowed headers | 204 with the CORS headers |
| For a path with no route | 204 with the CORS headers. `Access-Control-Allow-Methods` is `FallbackMethods` |
| From a refused origin | 204 with no CORS headers |
| For a method the path has no route for | 204 with no CORS headers |
| Naming a header that is not allowed | 204 with no CORS headers |

An allowed preflight gets `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods` and
`Access-Control-Max-Age`. It also gets `Access-Control-Allow-Headers` when it named headers.
`Access-Control-Allow-Methods` holds the requested method alone, when the path has a route for it.
`Access-Control-Allow-Headers` repeats the requested headers as the request wrote them. With
`CORS_ALLOWED_ORIGINS=https://app.example.com`, a preflight from that origin gets these headers:

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

Every requested header must be allowed, or the whole preflight is refused. The allowed request
headers are `Authorization`, `Content-Type`, `Accept`, `x-auth-token` and `x-amz-content-sha256`.
The filter compares them without regard to case. `AllowHeader` adds one.

A refused preflight is still a 204, with `Vary: Origin` and no CORS headers:

```http
OPTIONS /todos
Origin: https://evil.example.net
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type

HTTP/1.1 204 No Content
Vary: Origin
```

## Exposed headers

`Access-Control-Expose-Headers` lists `X-Correlation-Id` by default. Every response carries an
`X-Correlation-Id` header. `ExposeHeader` adds a header to the list. `ClearExposedHeaders` empties
the list. `Access-Control-Expose-Headers` is then not sent. The list goes on responses to actual
requests. A preflight does not carry it.

## Credentials

`AllowCredentials = true` adds `Access-Control-Allow-Credentials: true` to the preflight and to the
response to the actual request. `CORS_ALLOWED_ORIGINS` has no form for credentials. Only a
configuration in code sets them.

When `AllowAnyOrigin` is also `true`, the filter sends the request's origin in place of `*`. It also
leaves out `Access-Control-Allow-Credentials`.

## The `Vary` header

In an application that declares no `[Cors]`, every response to a request with an `Origin` header
carries `Vary: Origin`, whether the origin is allowed or refused. A preflight's 204 carries it too.
Once a route declares `[Cors]`, a declaring route's response and every preflight carry
`Vary: Origin`. A route with no declaration does not.

The filter adds `Origin` to the `Vary` values that other filters write. A compressed response
carries `Vary: Origin, Accept-Encoding`. [Compression](/guide/compression) covers when a response is
compressed.

## CORS on some routes

`[Cors]` is in `Hardened.Web.Runtime.Cors`. It goes on a handler method, a controller class or a
`[HardenedModule]` class. It takes no arguments. An application that declares `[Cors]` nowhere keeps
CORS on every request, as the sections above describe. Once a route or a module declares `[Cors]`,
CORS covers only the routes that a declaration reaches:

| Declaration | Covers |
|---|---|
| On a handler method | That handler |
| On a controller class | Every handler in the class |
| On a `[HardenedModule]` class | Every handler compiled in the module's project |
| A route registered through `IRouteRegistry` | Whatever its handler declares: on the controller method and its class, or on the lambda |

[Registered routes](/guide/registered-routes) covers routes registered through that interface.

The nearest declaration takes precedence: a method's over its class's, and either over the
module's. This also holds between `[Cors]` and `[Cors<TPolicy>]`, which
[A policy for some routes](#a-policy-for-some-routes) describes.

The template's `src/Todos.Host` project compiles no handler. `[Cors]` on its `Application` covers no
route. With that declaration alone, every route answers with no CORS headers. Every preflight is
refused. The build reports nothing, and nothing is logged.

A route that `[Cors]` covers answers cross-origin requests with the application's configuration.
The web module builds that configuration from `CORS_ALLOWED_ORIGINS`, unless the application
registers its own as [Allowing origins in code](#allowing-origins-in-code) shows.

This excerpt of `src/Todos/TodoController.cs` adds the `using` line and the two `[Cors]` lines to
the template's file:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Cors;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Cors]
    [Operation("listTodos")]
    [Get("/")]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();

    [Cors]
    [Operation("getTodo")]
    [Get("/{id}")]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

With `CORS_ALLOWED_ORIGINS=https://app.example.com`, `GET /todos/1` from `https://app.example.com`
answers as in the first example on this page. `Create` answers `POST /todos` and declares nothing.
Its preflight is refused:

```http
OPTIONS /todos
Origin: https://app.example.com
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type

HTTP/1.1 204 No Content
Vary: Origin
```

Its response carries no CORS headers and no `Vary`:

```http
POST /todos
Origin: https://app.example.com
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Write the docs","done":false}
```

Once a route declares `[Cors]`, a request gets one of these answers:

| The request | Answer |
|---|---|
| To a declaring route, from an origin its configuration allows | The CORS headers of [Cross-origin requests](#cross-origin-requests), and `Vary: Origin` |
| To a declaring route, from an origin its configuration refuses | `Vary: Origin` alone |
| To a route with no declaration | No CORS headers and no `Vary: Origin` |
| To a path no route answers, or with a method the path has no route for | 404 or 405, with no CORS headers and no `Vary: Origin` |
| A preflight for a declaring route, from an allowed origin, naming allowed headers | 204 with the CORS headers |
| A preflight for a route with no declaration, or for a path no route answers | 204 with `Vary: Origin` alone |

The filter still answers every preflight, before routing. It answers with the configuration of the
route that the preflight asks about. It does not use `FallbackMethods` for a path that no route
answers. The 405 for an `OPTIONS` request without `Access-Control-Request-Method` carries no CORS
headers either.

The filter that `[Cors]` installs runs before rate limiting, authorization and validation. A 401, a
429 and a validation 400 on a declaring route carry the CORS headers.
[The execution pipeline](/guide/execution-pipeline) gives the order of the filters.

## A policy for some routes

`[Cors<TPolicy>]` is also in `Hardened.Web.Runtime.Cors`. It covers routes as `[Cors]` does. Its
routes answer with the configuration that `AddCorsPolicy<TPolicy>` registered, in place of the
application's. `TPolicy` can be any type. It only names the policy.

`AddCorsPolicy<TPolicy>(configure)` is in the same namespace. It is an extension method on
`IServiceCollection`. It passes a new configuration to `configure` and registers it. A new
configuration allows no origin. It allows the five request headers of [Preflights](#preflights). It
exposes `X-Correlation-Id`. Its `MaxAgeSec` is 86400. A named policy does not read
`CORS_ALLOWED_ORIGINS`.

In this example, `src/Todos/Partners.cs` declares the type that names the policy:

```csharp
namespace Todos;

public sealed class Partners;
```

The library module registers the policy in its `ConfigureServices`. This excerpt of
`src/Todos/TodosLibrary.cs` adds the `using` line and the `AddCorsPolicy` line to the template's
code:

```csharp
using Hardened.Web.Runtime.Cors;

public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    services.AddCorsPolicy<Partners>(policy => policy.AllowOrigin("https://partner.example.com"));
}
```

In the same `src/Todos/TodoController.cs`, `Create` declares the policy:

```csharp
[Cors<Partners>]
[Operation("createTodo")]
[Post("/")]
public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
{
    if (await store.TitleExists(request.Title))
    {
        return new Conflict($"A todo titled '{request.Title}' already exists.");
    }

    var todo = await store.Add(request.Title);

    return new Created<Todo>(todo, $"/todos/{todo.Id}");
}
```

With those three changes and `CORS_ALLOWED_ORIGINS=https://app.example.com`, a preflight from the
partner is allowed:

```http
OPTIONS /todos
Origin: https://partner.example.com
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type

HTTP/1.1 204 No Content
Access-Control-Allow-Headers: content-type
Access-Control-Allow-Methods: POST
Access-Control-Allow-Origin: https://partner.example.com
Access-Control-Max-Age: 86400
Vary: Origin
```

The partner's request gets the policy's CORS headers:

```http
POST /todos
Origin: https://partner.example.com
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Access-Control-Allow-Origin: https://partner.example.com
Access-Control-Expose-Headers: X-Correlation-Id
Location: /todos/3
Vary: Origin

{"id":3,"title":"Write the docs","done":false}
```

A route whose `[Cors<TPolicy>]` names a policy that nothing registered answers every request with
500 and no body, with or without an `Origin` header. A preflight for it answers 500 with
`Vary: Origin`. The application still starts. Its other routes answer. Each of those requests logs
an `InvalidOperationException` with this message, where `Unregistered` is the type argument:

```text
[Cors<Unregistered>] names a policy that nothing registered. Register it with AddCorsPolicy<Unregistered>.
```

## Limits

No declaration in the application reaches the framework's routes: `/health/live`, `/health/ready`,
`/openapi.json` and `/docs`. Once a route declares `[Cors]`, they answer cross-origin requests with
no CORS headers. A preflight for them is refused.

The build does not read a declaration on a lambda registered through `IRouteRegistry` when it
decides whether routes declare CORS. An application whose only declarations are on such lambdas
keeps CORS on every request. The filter then answers every preflight with the application's
configuration, including a preflight for the lambda's route.

On Azure Functions, the worker never runs the startup services, so the filter is never installed. A
preflight is routed like any other `OPTIONS` request. The template's routes answer it 405 with an
`Allow` header. Without `[Cors]`, no response carries CORS headers. A route that declares `[Cors]`
sends the CORS headers to an allowed origin. A preflight for it still answers 405. The Azure
[Web applications](/azure/web) page covers what the worker skips.

## Next

| Page | Covers |
|---|---|
| [Authentication](/guide/authentication) | The credentials a cross-origin request carries |
| [The execution pipeline](/guide/execution-pipeline) | The middleware chain the filter runs in |
| [Modules](/guide/modules) | `ConfigureServices` and startup services |
