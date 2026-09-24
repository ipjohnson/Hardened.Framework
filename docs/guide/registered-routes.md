# Registered routes

A class that implements `IRouteRegistration` registers routes whose paths the application computes at startup. Each registered route points its path at a handler the build generated, so the path is the only part that comes from run time.

The example adds two files to `src/Todos` in a project made with `dotnet new hardened-web -n Todos`. `TenantCatalog.cs` holds a service that returns the active tenants:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Todos;

public interface ITenantCatalog
{
    Task<IReadOnlyList<string>> Active(CancellationToken cancellationToken);
}

[SingletonService]
public class TenantCatalog : ITenantCatalog
{
    public Task<IReadOnlyList<string>> Active(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["acme", "globex"]);
}
```

`TenantRoutes.cs` holds the registration:

```csharp
using Hardened.Web.Runtime.Routing;

namespace Todos;

public class TenantRoutes(ITenantCatalog tenants) : IRouteRegistration
{
    public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        foreach (var tenant in await tenants.Active(cancellationToken))
        {
            routes.Get($"/{tenant}/{{id}}", typeof(TodoController), nameof(TodoController.ById));
        }
    }
}
```

`TodoController.ById` from the template is declared `[Get("/{id}")]` in a module with `[BasePath("/todos")]`. It still answers `GET /todos/{id}`. `TenantRoutes` serves the same handler at one more path for each tenant that the catalog returns:

```http
GET /todos/acme/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

A tenant that the catalog does not return has no route:

```http
GET /todos/initech/1

HTTP/1.1 404 Not Found
Content-Length: 0
```

## Declaring a registration

`IRouteRegistration` and `IRouteRegistry` are in the namespace `Hardened.Web.Runtime.Routing`, in the package `Hardened.Web.Runtime`.

Implementing the interface is the whole declaration. The build registers every class in the project that names the interface in its base list, as a singleton. An abstract class is not registered.

::: warning
A registration class that inherits `IRouteRegistration` from a base class never runs. The build reports nothing. Name the interface in the class's own declaration, as in `class DerivedNamedRoutes : RoutesBase, IRouteRegistration`.
:::

`TenantRoutes` runs without a lifetime attribute. A registration class that is also marked `[SingletonService]` is registered once. Its `Register` runs once.

The container builds a registration class, so its constructor takes services. `TenantRoutes` receives `ITenantCatalog` this way. The registry's `ServiceProvider` property is the application's root service provider, for a dependency that the constructor cannot take. `Register` receives `CancellationToken.None`.

The module class can implement the interface itself. It takes its dependencies from `routes.ServiceProvider`. In this form, `src/Todos/TodosLibrary.cs` replaces `TenantRoutes.cs`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Hardened.Web.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration, IRouteRegistration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }

    public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        var tenants = routes.ServiceProvider.GetRequiredService<ITenantCatalog>();

        foreach (var tenant in await tenants.Active(cancellationToken))
        {
            routes.Get($"/{tenant}/{{id}}", typeof(TodoController), nameof(TodoController.ById));
        }
    }
}
```

The registry's methods come in a controller form and a lambda form:

| Method | Registers |
|---|---|
| `Get`, `Post`, `Put`, `Patch`, `Delete` `(path, controllerType, handlerMethod)` | A handler the build generated from a verb attribute on `controllerType.handlerMethod` |
| `Map(method, path, controllerType, handlerMethod)` | The same, with the verb as the first argument |
| `Get`, `Post`, `Put`, `Patch`, `Delete` `(path, lambda)` | A lambda |
| `Map(method, path, lambda)` | A lambda, with the verb as the first argument |

Each method returns the registry, so calls chain. `Map` upper-cases the verb it is given. The registry also has `Map(path, RegisteredRouteHandler)`, which generated code calls. An application does not call it.

## The registered path

A registered path uses the route template language of the verb attributes: tokens, constraints and the catch-all. [Routing](/guide/routing) covers the syntax.

::: v-pre
`[BasePath]` on the module prefixes a registered path, so `/{tenant}/{{id}}` answers at `/todos/acme/1`. A registered path can name a constraint that `[RouteConstraint]` declares in the same project. The module's `[CaseInsensitiveRoutes]` applies to registered paths too. A catch-all token takes the rest of the path.
:::

A registered route answers 405 with an `Allow` header for a verb it does not serve. A HEAD request reaches its GET handler. An attribute route does the same.

The controller form registers under the verb that the handler was declared with. `routes.Post(...)` naming a `[Get]` handler fails at startup.

The handler reads path tokens by name. The registered template has to declare every token that the handler's declared route declares, spelled the same way. `/acme/by/{todoId}` for `ById`, which is declared `/{id}`, fails at startup.

::: v-pre
The controller form is held to the token names only. A constraint in the registered template guards the route whatever the handler was declared with. A value that fails the constraint answers 404. Without a constraint, a value that the handler cannot convert answers 400. `GET /todos/acme/abc` answers 400 under `/{tenant}/{{id}}`.
:::

The handler reports the path it answered at. For a lambda registered at `/lab/path/{id:int}`, `IExecutionRequestHandlerInfo.Path` reads `/todos/lab/path/{id:int}`.

## Registering a lambda

The lambda form takes a path and a lambda. The build reads the lambda where it is written: its parameters, its return type and any attribute on it. It generates a handler from them. The build writes the handler into `<Module>.RegisteredLambdas.cs`.

A C# interceptor rewrites the call to one that passes that handler. `Hardened.Web.Runtime` sets the property that enables interceptors in any project that references the package. Nothing has to be added to the project file.

This `TenantRoutes.cs` replaces the first one and adds two lambdas for each tenant:

```csharp
using Hardened.Web.Runtime.Routing;

namespace Todos;

public class TenantRoutes(ITenantCatalog tenants) : IRouteRegistration
{
    public async ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        foreach (var tenant in await tenants.Active(cancellationToken))
        {
            routes.Get($"/{tenant}/{{id}}", typeof(TodoController), nameof(TodoController.ById));

            routes.Get(
                $"/{tenant}/summary",
                async (ITodoStore store) => new TenantSummary(tenant, (await store.All()).Count)
            );

            routes.Get(
                $"/{tenant}/{{id:int}}/title",
                async (int id, ITodoStore store) => (await store.Find(id))?.Title
            );
        }
    }
}

public record TenantSummary(string Tenant, int Todos);
```

A lambda can close over a variable of the loop. Each registration keeps its own value:

```http
GET /todos/acme/summary

HTTP/1.1 200 OK
Content-Type: application/json

{"tenant":"acme","todos":2}
```

A lambda parameter declares the path token of the same name. The title route reads its token through the `id` parameter:

```http
GET /todos/acme/1/title

HTTP/1.1 200 OK
Content-Type: application/json

"Read the generated code"
```

`GET /todos/acme/9/title` answers 404, because the lambda returns null for a GET. [Routing](/guide/routing) covers null returns.

## Lambda parameters

The type of a lambda parameter decides where it binds from:

| Parameter type | Binds from |
|---|---|
| `IExecutionContext`, `IExecutionRequest`, `IExecutionResponse` | The execution context |
| `IServiceProvider` | The request's service provider |
| `CancellationToken` | The request's token |
| Any other interface | The container |
| `string`, `bool`, the numeric types, `char`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, `Uri`, any enum, and each of these made nullable | The path token of the same name |
| Anything else, arrays included | The request body, read by the serializer |

The build ignores a binding attribute on a lambda parameter. `([FromQueryString] int page) => ...` and `([FromHeader("X-Tenant")] string tenant) => ...` each make the parameter a path token. The registry then refuses the route with `does not declare the token '{page}'`, and with the same message for `'{tenant}'`. A lambda cannot bind a query string value, a header or a cookie. It can read them through an `IExecutionContext` or `IExecutionRequest` parameter.

A `byte[]` or `Stream` parameter is read as a JSON body, not as the raw bytes. For a `byte[]` parameter, an `application/octet-stream` body answers 400. The JSON string `"YWJj"` is accepted. On a controller handler, the same parameter receives the raw bytes.

An enum token is read without regard to case, so `/lab/color/green` binds `Color.Green`. A name that the enum does not declare answers 400.

## Constraints on typed tokens

When a lambda reads a path token as a type that a route constraint can guarantee, the token has to carry one of the constraints in the table. The registry refuses the route without one.

| Lambda parameter | Constraints that satisfy it |
|---|---|
| `int` | `int`, `range` |
| `long` | `long`, `int`, `min`, `max`, `range` |
| `decimal`, `double`, `float` | `decimal`, `int`, `long`, `min`, `max`, `range` |
| `bool` | `bool` |
| `Guid` | `guid` |
| `DateOnly` | `date` |
| `DateTime`, `DateTimeOffset` | `datetime`, `date` |

The nullable form of each type takes the same constraints. A `string` token, an enum token and a token of any type not in the table need no constraint.

The constraint can sit anywhere in a chain. `{id:int}`, `{id:range(1,10)}` and `{id:min(1):int}` all satisfy an `int`. A constraint that `[RouteConstraint]` declares does not satisfy the rule alone. `{n:even}` fails for an `int`. `{n:int:even}` registers.

A value that fails the constraint answers 404, not 400:

```http
GET /todos/acme/abc/title

HTTP/1.1 404 Not Found
Content-Length: 0
```

::: v-pre
With `{{id}}` in place of `{{id:int}}` on the title route, the application does not start:
:::

```console
$ dotnet run --project src/Todos.Host
Unhandled exception. Hardened.Web.Runtime.Routing.RouteRegistrationException: 2 routes could not be registered:
  - '/acme/{id}/title' does not constrain the token '{id}', which the registered handler. Register it as '{id:int}' - or as '{id:range}', so a value it cannot read answers 404 rather than reaching the handler's binder
  - '/globex/{id}/title' does not constrain the token '{id}', which the registered handler. Register it as '{id:int}' - or as '{id:range}', so a value it cannot read answers 404 rather than reaching the handler's binder
```

## Attributes on a lambda

An attribute written on the lambda applies to its route as it does on a controller method:

| Attribute on the lambda | Effect on its route |
|---|---|
| A filter attribute of the application's own | Its filter runs |
| `[AuthorizeGrants]` | A request without a caller answers 401 |
| `[Compress]` | The response is compressed with gzip |
| `[CacheResponse<VaryByRoute>]` | Its filter is installed |
| `[Output<T>]` | The response is written through that output |
| `[Produces]` | The response carries the content type it names |

An attribute that is not valid on a method cannot be written on a lambda either. `[RequireAuthorization]` targets classes and assemblies. On a lambda, the build fails with `CS0592`.

`[Produces]` on the registration class covers every lambda that the class registers. The lambda's own attribute replaces it. In the loop of `TenantRoutes`, this line puts the attribute on one lambda:

```csharp
using Hardened.Requests.Abstract.Attributes;

routes.Get($"/{tenant}/name", [Produces("text/plain")] () => tenant);
```

```http
GET /todos/acme/name

HTTP/1.1 200 OK
Content-Type: text/plain

acme
```

## Lambda registrations the build cannot read

The build reports the error `HRDR014` for a lambda registration that it cannot read. The handler has to be a lambda written in the call. The build refuses a delegate held in a variable, a method group and a delegate returned from a method. The verb of a `Map` call has to be a constant. `src/Todos/UnreadableRoutes.cs` breaks both rules:

```csharp
using Hardened.Web.Runtime.Routing;

namespace Todos;

public class UnreadableRoutes : IRouteRegistration
{
    public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
    {
        Func<int, string> handler = id => id.ToString();

        routes.Get("/numbers/{id:int}", handler);

        var verb = "GET";

        routes.Map(verb, "/verbs", () => "ok");

        return default;
    }
}
```

The build fails:

```console
$ dotnet build src/Todos
src/Todos/UnreadableRoutes.cs(11,41): error HRDR014: This route cannot be registered: the handler has to be a lambda written here. The build reads the lambda to emit a handler for it, and a delegate held in a variable or returned from a method is not something it can read
src/Todos/UnreadableRoutes.cs(15,20): error HRDR014: This route cannot be registered: the verb has to be a constant. It is written into the handler's own information and into the table the route joins, so it cannot come from a variable
```

The registry's lambda overloads throw `InvalidOperationException` when they run. A call that the build did not rewrite, such as one in a project without `Hardened.Web.SourceGenerator`, throws when `Register` runs.

## Build and startup checks

The build checks the call site of a lambda registration. It also checks each registered handler as it checks any handler. The checks that need the path run at startup.

| Check | When | Message |
|---|---|---|
| The call site of a lambda registration can be read | Build | `HRDR014` |
| The controller method is a generated handler | Startup | `'/acme/store' names TodoStore.All, which is not a handler - it needs a verb attribute for the generator to emit one` |
| The verb matches the handler's declared verb | Startup | `'/acme/post/{id}' registers TodoController.ById under POST, and it is declared GET` |
| The template declares every token the handler reads | Startup | `'/acme/by/{todoId}' does not declare the token '{id}', which TodoController.ById reads - it was declared as '/{id}'` |
| A lambda's typed token carries a constraint that guarantees it | Startup | `'/acme/lambda/{id}' does not constrain the token '{id}', which the registered handler. Register it as '{id:int}' - or as '{id:range}', ...` |
| Every constraint name is declared | Startup | `'/todos/acme/n/{id:number}' is not routable: nothing declares a route constraint called 'number'` |
| No token is declared twice | Startup | `'/todos/acme/twice/{id}/{id}' declares the token 'id' more than once, so one of them could never be read` |
| No two registered routes share a verb and a path | Startup | `'/todos/acme/{id}' answers GET at the same path as '/todos/acme/{id}', so one of them could never be reached` |

Some messages name the path as written, and some name it with the base path.

The registry collects every failure. When the registrations finish, it throws one `RouteRegistrationException` that lists them all. The application does not start. On Kestrel, `StartAsync` throws the exception. The process exits with `Unhandled exception`. The exception's message starts `A route could not be registered:` for one failure and `N routes could not be registered:` for more. Each failure is on its own line. The exception's `Failures` property holds the failures as strings.

An exception that `Register` throws stops the application too. [Modules](/guide/modules) covers how a startup service fails.

## When registration runs

A startup service, `RouteRegistrationStartupService`, runs every registration and then publishes the registered-route table. It runs at the same time as the application's other startup services. A registration must not depend on another startup service's work. [Modules](/guide/modules) covers startup services.

On Kestrel, registration finishes before the host listens. On AWS Lambda, registration runs in the function's initialization, once per cold start. [Hosts](/guide/hosts) covers when each host runs the startup services.

The table is fixed once registration closes. A call to the registry after startup throws `InvalidOperationException`:

```text
Routes cannot be registered after startup, and '/lab/late/{id}' was. The table is built once and is immutable from then on.
```

Until registration closes, the table answers every request that reaches it with 503, `Retry-After: 1` and no body. On ASP.NET Core, `UseHardened` waits 15 seconds for the startup services and then serves. When a registration takes longer, the table answers 503 until the registration finishes. In the template's module order, every request answers 503 in that time, including the attribute routes and `/health/live`:

```http
GET /todos/1

HTTP/1.1 503 Service Unavailable
Content-Length: 0
Retry-After: 1
```

## The OpenAPI document

The document that the application serves describes each registered route at the path it was registered at. `GET /todos/acme/summary` and `GET /todos/acme/{id}/title` are path items in `/openapi.json`.

The document written at build time does not list registered routes. In the template, the file that `<HardenedOpenApiOutput>` writes is `src/Todos/openapi/Todos.json`. It holds only `/todos` and `/todos/{id}`. A client generated from that file, such as `src/Todos.Client`, has no call for a registered route.

::: v-pre
A path key carries no routing syntax. `/{tenant}/{{id:int}}/title` is published as `/todos/acme/{id}/title`. No key contains `:`. Two verbs registered at one path are one path item with two operations.
:::

Each registered operation gets an `operationId` made from the path it was registered at and its verb. Path segments become words. A token adds `By` and its name. The verb comes last. A lambda's operation is tagged with the name of the class that registered it, without a `Routes` suffix. `TenantRoutes` gives `Tenant`. A lambda that the module class registers is tagged with the module's name. The `TenantRoutes.cs` in [Registering a lambda](#registering-a-lambda) publishes these operations:

::: v-pre
| Registered | Path key | `operationId` | Tag |
|---|---|---|---|
| `routes.Get($"/{tenant}/{{id}}", typeof(TodoController), nameof(TodoController.ById))` for `acme` | `/todos/acme/{id}` | `todosAcmeByIdGet` | `Todo` |
| `routes.Get($"/{tenant}/summary", ...)` for `acme` | `/todos/acme/summary` | `todosAcmeSummaryGet` | `Tenant` |
| `routes.Get($"/{tenant}/{{id:int}}/title", ...)` for `acme` | `/todos/acme/{id}/title` | `todosAcmeByIdTitleGet` | `Tenant` |
:::

A controller-form route publishes the handler's own operation, with its summary, description, parameters, responses and tag. Only the `operationId` is new. The operation follows the handler's declared path. Under a registered template with a different constraint, it still publishes the declared path's responses.

A lambda's operation publishes the body it reads and the type it returns. A model that it uses is in `components/schemas`. A lambda token held to a constraint publishes a 404 and no 400. A lambda's `[Produces]` sets the content types that its 200 publishes.

[The OpenAPI document](/guide/openapi-document) covers the document and how to serve it.

## Limits

Declare every registration class in the project that holds the handlers it registers and whose module serves the document. The build gives each project its own handler catalog. Startup uses one of them:

| Registration | Result |
|---|---|
| Registrations in both the library and the host project | The library's registrations fail at startup with `'/acme/{id}' names TodoController.ById, which is not a handler` |
| A registration in the host project that names a library controller | It fails the same way |
| A registration in the host project, when the library serves `/openapi.json` | The host's document replaces the served document. Its title is `Application` in place of `TodosLibrary`. It lists only the host's paths |

Do not register a route that an attribute route also matches. The registry accepts it. The order of the module attributes on the application module decides which one answers. In the template's order, `[KestrelRuntime]` before `[TodosLibrary]`, the registered route answers. With `[TodosLibrary]` first, the attribute route answers. The served document then holds the path key twice.

The generated `Routes` and `Links` classes do not include registered routes. [Route links](/guide/route-links) covers those classes.

## Next

| Page | Covers |
|---|---|
| [Routing](/guide/routing) | Route attributes, the template syntax and constraints |
| [Route links](/guide/route-links) | Typed links to declared routes |
| [Parameter binding](/guide/parameter-binding) | Where a controller handler's parameters bind from |
| [The OpenAPI document](/guide/openapi-document) | The document the application serves |
| [Modules](/guide/modules) | Startup services |
