# The execution pipeline

Every handler runs as the last step of a chain of filters. A filter implements `IExecutionFilter`. It does its work around `chain.Next()`, which runs the rest of the chain. HTTP handlers and trigger handlers build their chain the same way.

The filter in this example times the handler and sends the result in a `Server-Timing` header. In a project made with `dotnet new hardened-web -n Todos`, the filter goes in `src/Todos/ServerTimingFilter.cs`:

```csharp
using System.Diagnostics;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

public class ServerTimingFilter : IExecutionFilter
{
    public async Task Execute(IExecutionChain chain)
    {
        var start = Stopwatch.GetTimestamp();

        await chain.Next();

        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        chain.Context.Response.Headers["Server-Timing"] = $"handler;dur={elapsed:F1}";
    }
}
```

An attribute that implements `IRequestFilterProvider` puts a filter on the handler it is written on. This one goes in `src/Todos/ServerTimingAttribute.cs`:

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Todos;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ServerTimingAttribute : Attribute, IRequestFilterProvider
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        yield return new RequestFilterInfo(
            _ => new ServerTimingFilter(),
            FilterOrder.DefaultValue,
            nameof(ServerTimingFilter)
        );
    }
}
```

`[ServerTiming]` goes on the template's `ById` handler in `src/Todos/TodoController.cs`:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [ServerTiming]
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

The response carries the header:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
Server-Timing: handler;dur=1.2

{"id":1,"title":"Read the generated code","done":true}
```

## Write a filter

`IExecutionFilter` and `IExecutionChain` are in `Hardened.Requests.Abstract.Execution`. `IRequestFilterProvider`, `RequestFilterInfo` and `FilterOrder` are in `Hardened.Requests.Abstract.RequestFilter`. Both namespaces come with `Hardened.Web.Runtime`. A project made from the `hardened-web` template needs no other package.

The chain that a filter receives has these members:

| Member | Meaning |
|---|---|
| `Next()` | Runs the next filter, and through it the rest of the chain. After the last filter it returns a completed task |
| `Context` | The request's `IExecutionContext` |
| `Fork(context)` | Returns a chain that runs the remaining filters again, against `context` |
| `IsLastFilter` | True when no filter is left to run |

A filter that returns without calling `Next()` ends the chain there. The filters behind it and the handler do not run.

The constructor of `RequestFilterInfo` takes these arguments:

| Argument | Meaning |
|---|---|
| `filterFunc` | A function from the request's `IExecutionContext` to the filter |
| `order` | Where the filter runs. Null runs it at `FilterOrder.DefaultValue` |
| `name` | The filter's name in the `Hardened.Requests.Pipeline` log. Optional |

`filterFunc` runs each time a request reaches the filter, with that request's context. A filter that needs a service resolves it from `context.RequestServices`.

A handler's chain is built on the first request that reaches the handler. `GetFilters` runs once for each handler, when its chain is built. It receives the handler's `IExecutionRequestHandlerInfo`: its path, verb, handler type and the attributes written on it. It can return no filter for a handler it does not apply to.

## The execution context

Every filter and the handler of a request share one `IExecutionContext`:

| Member | Holds |
|---|---|
| `Request` | The method, path, headers, query string, path tokens, cookies and body |
| `Response` | The status, headers, cookies, content type, the handler's return value and any failure |
| `RequestServices` | The request's service scope |
| `RootServiceProvider` | The application's service provider |
| `KnownServices` | The serialization, string conversion and form reading services |
| `CallerPrincipal` | The caller. It is `AnonymousCallerPrincipal` until authentication sets one |
| `CorrelationId` | The request's id, which is the value of the `X-Correlation-Id` response header |
| `HandlerInfo` | The handler's `IExecutionRequestHandlerInfo`. Its `Path` is the route template with the base path, such as `/todos/lab/context` |
| `HandlerInstance` | The object the handler method is called on. It is set at `FilterOrder.HandlerCreation` |
| `RequestMetrics` | An `IMetricLogger` for this request |
| `StartTime` | A `MachineTimestamp` taken when the request began |
| `CancellationToken` | What the request's work stops on. A request timeout replaces it for its span, as [Request timeouts](/guide/request-timeouts) describes |
| `DefaultOutput` | When set, a function that writes the response in place of the serializer |

A filter uses these members of `Response`:

| Member | Holds |
|---|---|
| `ResponseValue` | What the handler returned, before it is serialized |
| `ExceptionValue` | A failure to answer with in place of the value |
| `Refused` | True when `ExceptionValue` is set |
| `Status` | The status, or null for the one serialization chooses |
| `Headers` | The response headers |
| `ShouldSerialize` | False when the response needs no serializing, such as when the handler wrote the body itself |
| `ResponseStarted` | True once bytes have been sent |

The filter at `FilterOrder.Serialization` writes the response after the rest of the chain returns. A filter ordered behind `Serialization` runs before the response is written. After `Next()` returns, it can set a header or replace `ResponseValue`. The response carries the change. The first example's filter sets `Server-Timing` this way.

A filter ordered ahead of `Serialization` gets control back after the response was written. A `ResponseValue` that it sets then is not sent. On Kestrel, setting a header then throws `InvalidOperationException` with the message "Headers are read-only, response has already started." The request is logged as failed. The client receives the response without the header.

## Attach a filter

These declarations attach a filter:

| Declaration | Covers | A nearer declaration replaces it | In the OpenAPI document |
|---|---|---|---|
| The attribute on a handler method | That handler | Not applicable | Yes |
| The attribute on a controller class | Every handler in the class | No. On both the class and a method, the method's handler gets two filters | Yes |
| The attribute on a `[HardenedModule]` class | Every handler compiled in the module's project | Yes | Yes |
| `services.AddGlobalFilter(provider, when)` | Every handler in the application that `when` accepts, including the framework's | No | No |
| `IGlobalFilterRegistry.RegisterFilter` in a startup service | Every handler, or each one a function returns a filter for | No | No |

### Every handler in a project

A filter attribute on the module class covers every handler compiled in the same project as the module. It does not cover the framework's handlers or handlers compiled in another project.

::: warning
An attribute on the host project's `Application` module reaches no handler in `src/Todos`. The build reports nothing. Put the attribute on the module that holds the routes, or register it with `AddGlobalFilter`.
:::

Any filter attribute works on a module, including one the application wrote, unless it sets an enum property to a value other than the enum's zero value. That fails the build with `CS0266` in the generated module code, and [Attributes](/reference/attributes) lists the attributes it reaches. A handler that carries the same attribute type on its method or class gets only its own. A generic attribute closed over two different types is two attribute types. `[Cors]` and `[Cors<TPolicy>]` are an exception to these rules. Only the nearest declaration installs a filter, even when a class and one of its methods both declare one, or a module declares `[Cors]` and a handler declares `[Cors<TPolicy>]`. [CORS](/guide/cors) covers them.

Here `[ServerTiming]` is on `TodosLibrary`, the template's module in `src/Todos/TodosLibrary.cs`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
[ServerTiming]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

### Every handler in the application

`services.AddGlobalFilter(provider, when)` applies a filter provider to every handler in the application. It is an extension method in `Hardened.Requests.Runtime.Filters`. `when` is optional. It is asked once for each handler.

`AddGlobalFilter` covers the framework's handlers too, such as `/health/live`, `/openapi.json` and `/docs`. A handler that declares the same attribute gets two filters.

In `src/Todos/TodosLibrary.cs`, this module adds the filter to every GET handler:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddGlobalFilter(new ServerTimingAttribute(), when: handler => handler.Method == "GET");
    }
}
```

### Every handler, from a startup service

`IGlobalFilterRegistry` registers a filter for every handler at startup. It is in `Hardened.Requests.Abstract.RequestFilter`. Resolve it in an `IStartupService`. Its `RegisterFilter` method has two forms:

| Call | Adds |
|---|---|
| `RegisterFilter(filter, order)` | One filter instance, shared by every request of every handler. `order` defaults to `FilterOrder.DefaultValue` |
| `RegisterFilter(handlerInfo => ...)` | The `RequestFilterInfo` that the function returns. The function runs once for each handler. Null skips that handler |

The startup service needs a lifetime attribute, such as `[SingletonService]`, to be registered. Without one, it never runs. [Modules](/guide/modules) covers startup services.

`RequireTenantFilter` is the filter from [Refuse a request](#refuse-a-request). This startup service goes in `src/Todos/RegisterFilters.cs`:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Todos;

[SingletonService]
public class RegisterFilters : IStartupService
{
    public Task<bool> Startup(IServiceProvider rootProvider)
    {
        var registry = rootProvider.GetRequiredService<IGlobalFilterRegistry>();

        registry.RegisterFilter(new ServerTimingFilter(), FilterOrder.DefaultValue);

        registry.RegisterFilter(handlerInfo =>
            handlerInfo.Method == "POST"
                ? new RequestFilterInfo(
                    _ => new RequireTenantFilter(),
                    FilterOrder.After + FilterOrder.GrantAuthorization,
                    nameof(RequireTenantFilter)
                )
                : null
        );

        return Task.FromResult(true);
    }
}
```

With this class, `GET /todos/1` and `/health/live` carry `Server-Timing`. A `POST /todos` without an `X-Tenant` header answers 400.

### What the OpenAPI document shows

The build reads a filter attribute on a method, a class or a module. The statuses and headers that the attribute declares reach the OpenAPI document. [Refuse a request](#refuse-a-request) shows an attribute that declares them.

`AddGlobalFilter` and `IGlobalFilterRegistry` add filters at run time. The OpenAPI document does not show them.

## Filter order

`FilterOrder` names each stage with an integer. The chain runs from the lowest number to the highest. A filter with a lower number runs earlier and wraps everything after it. A filter that states no order runs at `FilterOrder.DefaultValue`, after every stage and before the handler. The handler runs at `EndPointInvoke`. It never calls `Next()`. A filter ordered behind it is never reached.

These are the stages and the shipped filters that run at each:

| Stage | Value | What runs there |
|---|---|---|
| `HandlerCreation` | -10000 | `InstanceFilter`, which resolves the handler's class from the request's services |
| `Before + RateLimitTransport` | 500 | `CorsRouteFilter`, for `[Cors]` and `[Cors<TPolicy>]` |
| `RateLimitTransport` | 1000 | `RateLimitFilter` for `[RateLimit]` with the default scope |
| `Authentication` | 2000 | No shipped filter. Authentication runs before the filter chain |
| `RateLimitPrincipal` | 3000 | `RateLimitFilter` for `[RateLimit(Scope = RateLimitScope.Principal)]` |
| `GrantAuthorization` | 4000 | `AuthorizationFilter` for a requirement over grants alone |
| `Conditional` | 5000 | `ConditionalGetFilter` |
| `Before + ResponseCache` | 5500 | `ResponseCompressionFilter`, and `RequestDecompressionFilter` on every handler |
| `ResponseCache` | 6000 | `ResponseCacheFilter` |
| `BeforeSerialization` | 6500 | `TimeoutFilter` and `CacheControlFilter` |
| `Serialization` | 7000 | `IoFilter`, which binds the request before calling `Next()` and writes the response after |
| `Validation` | 8000 | `ValidationFilter` |
| `Authorization` | 9000 | `AuthorizationFilter` for a requirement that reads bound parameters |
| `Retry` | 10000 | `RetryFilter` |
| `DefaultValue` | 100000 | A filter that states no order |
| `EndPointInvoke` | 200000 | The handler. `EndPointHandlers` has the same value |

`FilterOrder.Before` is -500. `FilterOrder.After` is 500. Each is half the distance between two stages. `FilterOrder.After + FilterOrder.GrantAuthorization` is 4500, between `GrantAuthorization` and `Conditional`.

`Before` and `After` do not stack. `Before + Before + Serialization` is 6000, which is `ResponseCache` itself. Name the earlier stage instead. `Before + Serialization` and `After + ResponseCache` are both 6500. `BeforeSerialization` names that position.

Filters at the same order run in the order they were added. Filters registered for every handler come first, then the module's attributes, then the handler's own attributes.

### Conditional requests, compression and the response cache

The conditional request filter runs outside the compression filter. The compression filter runs outside the response cache. The cache control filter runs inside the response cache.

The response cache stores the body before compression. The compression filter decides for each caller whether a cache hit is compressed. The conditional request filter answers a conditional request ahead of the response cache and compression, on a cache hit as well as a miss. A 304 has no body to compress.

On a cache hit, the cache control filter does not run. The stored response carries the header that it set on the miss.

[Response caching](/guide/response-caching), [Conditional requests](/guide/conditional-requests) and [Compression](/guide/compression) cover each feature.

## Refuse a request

A filter ordered ahead of `Serialization` refuses a request by setting `Response.ExceptionValue` and calling `Next()`. The filter at `Serialization` then writes the refusal without reading the body or running the handler. Validation and the other filters behind `Serialization` do not run for a refused request.

`StatusCodeException` names the status. Its message becomes the body's `message`. It is in `Hardened.Requests.Abstract.Errors`.

Requests that an earlier filter refused still reach a filter ordered ahead of `Serialization`. `Response.Refused` is true for them.

This filter refuses a request without an `X-Tenant` header. It goes in `src/Todos/RequireTenantFilter.cs`:

```csharp
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

public class RequireTenantFilter : IExecutionFilter
{
    public Task Execute(IExecutionChain chain)
    {
        var context = chain.Context;

        if (!context.Request.Headers.ContainsKey("X-Tenant"))
        {
            context.Response.ExceptionValue = new StatusCodeException(
                400,
                message: "The X-Tenant header is required."
            );
        }

        return chain.Next();
    }
}
```

The attribute goes in `src/Todos/RequireTenantAttribute.cs`:

```csharp
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
[AnswersStatus(400, typeof(ErrorModel))]
[ReadsHeader("X-Tenant")]
public class RequireTenantAttribute : Attribute, IRequestFilterProvider
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        yield return new RequestFilterInfo(
            _ => new RequireTenantFilter(),
            FilterOrder.After + FilterOrder.GrantAuthorization,
            nameof(RequireTenantFilter)
        );
    }
}
```

`[RequireTenant]` goes on the template's `All` handler in `src/Todos/TodoController.cs`:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [RequireTenant]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

A request without the header gets the refusal:

```http
GET /todos

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"StatusCodeException","message":"The X-Tenant header is required.","details":""}
```

A request with the header reaches the handler:

```http
GET /todos
X-Tenant: acme

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

These attributes on the filter attribute's class reach the OpenAPI document of every operation that carries it:

| Attribute | Publishes | Namespace |
|---|---|---|
| `[AnswersStatus(status, typeof(body))]` | The status, with that body | `Hardened.Requests.Abstract.Responses` |
| `[ReadsHeader(name)]` | The header, as an optional parameter | `Hardened.Web.Runtime.Responses` |

With both, `GET /todos` gains an `X-Tenant` header parameter and a 400 with `ErrorModel`. `ErrorModel` is in `Hardened.Requests.Abstract.Errors`. [The OpenAPI document](/guide/openapi-document) lists what the shipped filters publish.

The client receives these responses from a GET handler:

| The filter | Ahead of `Serialization` | Behind `Serialization` |
|---|---|---|
| Sets `ExceptionValue` and calls `Next()` | The refusal, with its status and body. The request body is not read. The handler does not run | The refusal, with its status and body. The handler still runs |
| Sets `ExceptionValue` and returns | The refusal, with its status and body | The refusal, with its status and body |
| Throws | 500 with an empty body, logged as a failed request | The exception's response: the status of a `StatusCodeException`, or 500 |
| Sets `Status` and returns | That status, with an empty body | The status for a null value replaces it. The GET answers 404 |

[Routing](/guide/routing) covers the status for a null value.

## Retry a handler

`[Retry]` runs the handler again when an attempt fails. It goes on a method, a class or a module. It is in `Hardened.Requests.Runtime.Filters`.

Its filter runs at `FilterOrder.Retry`, behind `Serialization` and `Authorization`. The filter at `Serialization` writes the response once, after the last attempt. A refusal is never retried. Every attempt runs on the same handler instance. Each attempt runs the rest of the chain again through `Fork`: the filters behind the retry filter, then the handler.

Here `[Retry]` is on the template's `All` handler:

```csharp
using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [Retry(Attempts = 4, SleepTime = 200)]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

`[Retry]` has these properties:

| Property | Default | Meaning |
|---|---|---|
| `Attempts` | 3 | Attempts in total, including the first. 1 or less runs the handler once |
| `Retries` | 3 | Another name for `Attempts` |
| `SleepTime` | 500 | Milliseconds. The wait before attempt n is random, from 0 to `SleepTime` × 2^(n-2). 0 does not wait |
| `TotalBudget` | 10000 | Milliseconds across all attempts. No attempt starts after it. 0 sets no bound |
| `AllowNonIdempotent` | `false` | Retries a request of any method |

Without `AllowNonIdempotent`, only GET, HEAD, PUT, DELETE, OPTIONS and TRACE are retried. A trigger handler is retried only with `AllowNonIdempotent = true`. A trigger's request method is the scheme of its source, such as `INVOKE` or `QUEUE`, which is not in that list.

These failures are not retried: `OperationCanceledException`, `BadRequestException`, `FormatException`, and any exception that implements `IStatusCodeException` with a 4xx status. A failure is not retried once the response has started or the request is cancelled. The filter at `Serialization` writes the last failure.

On a handler that returns `IAsyncEnumerable<T>`, a retry covers the call. A failure while the sequence is enumerated is not retried.

## Log the filter chain

The `Hardened.Requests.Pipeline` log category records each handler's chain at `Debug`: every filter and its order, in the order they run. One line is written for each handler, when its chain is built on the first request. The entry's event id is 78010. `ExecutionHelper.FilterChainLogCategory`, in `Hardened.Requests.Runtime.Execution`, holds the category name.

This call replaces the template's `services.AddLogging(...)` line in `src/Todos.Host/Program.cs`:

```csharp
using Microsoft.Extensions.Logging;

services.AddLogging(logging => logging
    .AddSimpleConsole(options => options.SingleLine = true)
    .AddFilter("Hardened.Requests.Pipeline", LogLevel.Debug));
```

With `[ServerTiming]` on `ById` from the first example, the first `GET /todos/1` logs this line:

```console
dbug: Hardened.Requests.Pipeline[78010] GET /todos/{id} filter chain: InstanceFilter@-10000, RequestDecompressionFilter@5500, IoFilter@7000, ValidationFilter@8000, ServerTimingFilter@100000, AsyncInvokeWithParametersFilter@200000
```

The log names each filter this way:

| The filter | Its name in the log |
|---|---|
| Has a `name` in its `RequestFilterInfo` | That name |
| Has no `name` | The type that registered it, such as `RegisterFilters@10001` |
| Was added with `RegisterFilter(filter, order)` | The filter's type |
| Is generic | The type's name without its arity, such as `InstanceFilter` |

## Fork a chain

`chain.Fork(context)` returns a chain that starts where this one is and runs against `context`. Calling `Next()` on it runs every remaining filter again. Calling `Next()` a second time on the same chain does not run them again. It runs whatever is left. After a full pass, it returns at once.

`IExecutionContext.Clone`, `IExecutionRequest.Clone` and `IExecutionResponse.Clone` return a copy with the replacements they are given. A cloned context keeps the caller and the correlation id.

The retry filter forks once for each attempt. The batch trigger filter forks once for each record, with a cloned context.

## The middleware chain

Each request first runs a middleware chain, which `IMiddlewareService` holds. It runs in this order:

| Order | What runs |
|---|---|
| 1 | A filter that writes a response a middleware decided |
| 2 | A filter that sets the `X-Correlation-Id` header |
| 3 | The middleware that startup services add, including CORS. Authentication is added when a principal source is registered |
| 4 | Routing, which the host adds after the startup services run. The handler's filter chain runs inside it |

Authentication runs in the middleware chain. Every filter therefore sees the caller in `CallerPrincipal`. [Authentication](/guide/authentication) covers how it sets the caller.

A startup service adds middleware with `IMiddlewareService.Use`. The interface is in `Hardened.Requests.Abstract.Middleware`. Middleware runs for every request, including one that no route matches. It runs before routing. `HandlerInfo` is null there.

This middleware adds an `Api-Version` header. It goes in `src/Todos/ApiVersionMiddleware.cs`, with the startup service that installs it:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Todos;

public class ApiVersionMiddleware : IExecutionFilter
{
    public Task Execute(IExecutionChain chain)
    {
        chain.Context.Response.Headers["Api-Version"] = "2";

        return chain.Next();
    }
}

[SingletonService]
public class RegisterMiddleware : IStartupService
{
    public Task<bool> Startup(IServiceProvider rootProvider)
    {
        rootProvider.GetRequiredService<IMiddlewareService>().Use(_ => new ApiVersionMiddleware());

        return Task.FromResult(true);
    }
}
```

The header is on a handler's response and on the 404 for a path that no route matches:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
Api-Version: 2

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /nothing-here

HTTP/1.1 404 Not Found
Content-Length: 0
Api-Version: 2
```

## Limits

On Azure Functions, startup services never run. A filter that a startup service registers through `IGlobalFilterRegistry` is never installed there. Middleware that a startup service adds through `IMiddlewareService` is never installed there either. Filter attributes and `AddGlobalFilter` are unaffected. They are read when the chain is built.

## Next

| Page | Covers |
|---|---|
| [Modules](/guide/modules) | Startup services and `ConfigureServices` |
| [Request timeouts](/guide/request-timeouts) | The timeout filter and its budget |
| [Rate limiting](/guide/rate-limiting) | A filter that refuses a request before its body is read |
| [The OpenAPI document](/guide/openapi-document) | What the shipped filters publish in the document |
| [Sending requests](/guide/testing-web) | Tests that run every filter |
