# Request timeouts

`[Timeout]` bounds how long an operation may take. When the budget runs out, the `CancellationToken`
that the handler takes is cancelled. A handler that passes the token to the work it awaits stops,
and the caller gets `504 Gateway Timeout`.

```csharp
using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Get("/export")]
    [Timeout(Milliseconds = 2000)]
    public async Task<IReadOnlyList<Todo>> Export(
        ITodoStore store,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        return await store.All();
    }
}
```

The examples run in a project from `dotnet new hardened-web -n Todos`. Its library module carries
`[BasePath("/todos")]`, so `[Get("/export")]` answers `GET /todos/export`. `Task.Delay` stands for
five seconds of work. The request is answered after 2 seconds:

```http
GET /todos/export

HTTP/1.1 504 Gateway Timeout
Content-Type: application/json

{"type":"GatewayTimeout","message":"The server did not finish this request in time.","details":""}
```

`[Timeout]` is in the namespace `Hardened.Requests.Runtime.Filters`, in the package
`Hardened.Requests.Runtime`. `Hardened.Web.Runtime` brings that package. The projects of the
`hardened-web` template reference `Hardened.Web.Runtime`, so they need no other package. The
attribute needs no registration.

## Properties

`[Timeout]` goes on a method, a class or an assembly. With no `Milliseconds`, it bounds the
operation at 30 seconds.

| Property | Default | Effect |
|---|---|---|
| `Milliseconds` | 30000 | How long the operation may take |
| `Status` | 504 | The status sent when the budget runs out |
| `RetryAfterSeconds` | 0 | Seconds for a `Retry-After` header. 0 sends none |
| `Deadline` | `true` | Whether `IRequestDeadline` reports the budget |

## Where a budget is declared

A budget can be declared in these places, nearest first:

| Declaration | Covers |
|---|---|
| `[Timeout]` on a method | That operation |
| `[Timeout]` on a class | Every handler in the class |
| `[assembly: Timeout]` | Every handler compiled in the same project |
| `[RequestTimeouts(n)]` or `[Enable<RequestTimeouts>]` on a module | Every handler in the application |

The nearest declaration is the handler's budget. [Budgets in a contract](#budgets-in-a-contract)
gives the order for a contract-first handler.

Budgets do not combine. A method's budget replaces its class's budget, also when it is longer. In
`src/Todos/ReportController.cs`, `Archive` declares a longer budget than its class:

```csharp
using Hardened.Requests.Runtime.Filters;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[Timeout(Milliseconds = 5000)]
public class ReportController
{
    [Get("/reports/open")]
    public async Task<int> Open(ITodoStore store) =>
        (await store.All()).Count(todo => !todo.Done);

    [Get("/reports/archive")]
    [Timeout(Milliseconds = 60_000)]
    public async Task<IReadOnlyList<Todo>> Archive(ITodoStore store) =>
        (await store.All()).Where(todo => todo.Done).ToList();
}
```

A new file, `src/Todos/Timeouts.cs`, holds the assembly's declaration:

```csharp
using Hardened.Requests.Runtime.Filters;

[assembly: Timeout(Milliseconds = 10_000)]
```

With `TodoController`, `ReportController` and `Timeouts.cs` in the library, the handlers get these
budgets:

| Request | Budget | Declared by |
|---|---|---|
| `GET /todos/export` | 2000 ms | The method |
| `GET /todos/reports/open` | 5000 ms | The class |
| `GET /todos/reports/archive` | 60000 ms | The method, which beats its class |
| `GET /todos` and the template's other handlers | 10000 ms | The assembly |

`[assembly: Timeout]` covers only the handlers compiled in its own project. Written in the host
project, it does not cover the handlers of the library that the host references. In a library, it
beats the application default for that library's handlers.

::: warning
`[Timeout]` on a `[HardenedModule]` class compiles and bounds nothing. The handlers that the module
covers get no budget from it. The OpenAPI document shows no budget for them. The build reports
nothing. Use `[assembly: Timeout]` to bound every handler in a project.
:::

A handler that nothing bounds has no budget. The timeout filter, `TimeoutFilter`, is not installed
for it. Its `CancellationToken` is the host's own token. On Kestrel, that token is cancelled when
the client disconnects. When the token is cancelled, the request is answered 500 and logged at
`Error` as a failed request.

## The application default

`[Enable<RequestTimeouts>]` on a module bounds every handler that no nearer declaration covers at
30 seconds. `[RequestTimeouts(5000)]` sets the number, in milliseconds. `[Enable<RequestTimeouts>]`
takes no number. With both written, the smaller budget applies.

`RequestTimeouts` and the `[RequestTimeouts]` attribute are in `Hardened.Requests.Runtime.Filters`.
`[Enable<T>]` is in `Hardened.Shared.Runtime.Attributes`. The application module in
`src/Todos.Host/Application.cs` sets a default of 5000 ms:

```csharp
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[RequestTimeouts(5000)]
[TodosLibrary]
public partial class Application;
```

The default covers every handler in the application: the host project's, those of every referenced
library, and the framework's own, such as `/openapi.json` and `/health/live`. Written on a library
module, `[RequestTimeouts]` sets the same default for the whole application, host project included.

When a default budget runs out, the caller always gets 504, with no `Retry-After`. The OpenAPI
document does not show the default. It lists no 504 and no `x-hardened-timeout` for the handlers
that the default bounds. [The OpenAPI document](/guide/openapi-document) describes what a declared
budget publishes.

## Conventions

An `IRequestTimeoutConvention` states a budget for a set of handlers by a rule. Its `Apply` method
receives a handler's `IExecutionRequestHandlerInfo`. `Apply` returns a `TimeoutPolicy`, or null to
leave the handler alone.
`TimeoutPolicy(Milliseconds, Status = 504, RetryAfterSeconds = 0, Deadline = true)` holds the same
four values as `[Timeout]`.

`IRequestTimeoutConvention` and `TimeoutPolicy` are in `Hardened.Requests.Abstract.Timeouts`.
`IExecutionRequestHandlerInfo` is in `Hardened.Requests.Abstract.Execution`.
`ReadTimeoutConvention`, in `src/Todos/ReadTimeoutConvention.cs`, returns a policy for every `GET`
handler:

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Timeouts;

namespace Todos;

public class ReadTimeoutConvention : IRequestTimeoutConvention
{
    public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handlerInfo) =>
        handlerInfo.Method == "GET" ? new TimeoutPolicy(10_000) : null;
}
```

A convention is registered as a singleton `IRequestTimeoutConvention`. An application can register
several. The `ConfigureServices` method of the template's `src/Todos/TodosLibrary.cs` registers this
one, with `using Hardened.Requests.Abstract.Timeouts;` added to the file:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

    services.AddSingleton<IRequestTimeoutConvention, ReadTimeoutConvention>();
}
```

A convention is asked once for each handler, when the first request to the handler builds its
chain. It is asked about every handler in the application, including the framework's own, such as
`/openapi.json`, `/docs` and the health endpoints. `Apply` can read these members of its
`handlerInfo`:

| Member | Value |
|---|---|
| `Path` | The route template with the base path, such as `/todos/{id}` |
| `Method` | The verb in capitals, such as `GET` |
| `Timeout` | Only the budget declared on the method or its class. It is null for a handler bounded only by its assembly or the application default |

A convention can only shorten a budget. It bounds a handler that has none, shortens a longer budget
and leaves a shorter one alone. With `ReadTimeoutConvention` registered, and with no `Timeouts.cs`
and no application default, the handlers get these budgets:

| Request | Declared | With `ReadTimeoutConvention` |
|---|---|---|
| `GET /todos` | None | 10000 ms |
| `GET /todos/export` | 2000 ms | 2000 ms |
| `GET /todos/reports/open` | 5000 ms | 5000 ms |
| `GET /todos/reports/archive` | 60000 ms | 10000 ms |
| `POST /todos` | None | None |
| `GET /health/live` | None | 10000 ms |

When the convention's budget is the shorter, its whole policy applies, status and `Retry-After`
included.

## What the caller receives

When the handler stops at the deadline, the caller gets the status that `Status` names, with a JSON
body. `Status` decides the status line and the body's `type`:

| `Status` | Status line | Body `type` |
|---|---|---|
| 504, the default | `504 Gateway Timeout` | `GatewayTimeout` |
| 503 | `503 Service Unavailable` | `ServiceUnavailable` |
| Any other, such as 429 | That status | `GatewayTimeout` |

The body's `message` is always "The server did not finish this request in time." Its `details` is
empty. `RetryAfterSeconds` adds a `Retry-After` header with that many seconds, whatever the status.
This version of the first example's `Export` sets `Status` and `RetryAfterSeconds`:

```csharp
[Get("/export")]
[Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]
public async Task<IReadOnlyList<Todo>> Export(
    ITodoStore store,
    CancellationToken cancellationToken
)
{
    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

    return await store.All();
}
```

```http
GET /todos/export

HTTP/1.1 503 Service Unavailable
Content-Type: application/json
Retry-After: 30

{"type":"ServiceUnavailable","message":"The server did not finish this request in time.","details":""}
```

A contract-first operation gets the same body, whatever error body its contract declares for other
statuses. [What the budget covers](#what-the-budget-covers),
[A streamed response](#a-streamed-response) and [Limits](#limits) give the cases that answer
otherwise.

## Logs and metrics

A request whose budget runs out is logged at `Warning`, with event id 78005. The line names the
budget and the status. The first example's request logs this line:

```console
warn: Hardened.Requests.Runtime.Logging.RequestLogger[78005] GET /todos/export did not finish inside its 2000 ms budget, answered 504
```

The metric `RequestTimedOut` records 1 for each request whose budget ran out. It also records a
request whose handler ignored the token and answered late.

The metric goes to the application's `IMetricLoggerProvider`. The default provider,
`NullMetricLoggerProvider`, discards it. With `MeterMetricLoggerProvider` registered, the metric is
the instrument `RequestTimedOut`, with the unit `{count}`, on the meter `Hardened.Requests`. That
provider is in `Hardened.Requests.Runtime.Diagnostics`. `IMetricLoggerProvider` is in
`Hardened.Shared.Runtime.Metrics`. In `src/Todos.Host/Program.cs`, the two `services` lines go after
`new Application().PopulateServiceCollection(services);`:

```csharp
using Hardened.Requests.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;

services.RemoveAll<IMetricLoggerProvider>();
services.AddSingleton<IMetricLoggerProvider, MeterMetricLoggerProvider>();
```

## Reading the budget in a handler

`IRequestDeadline` reports the running request's budget. It is in
`Hardened.Requests.Abstract.Timeouts`. It is registered as a singleton. A handler takes it as a
parameter or through its constructor.

| Member | Type | Value |
|---|---|---|
| `Deadline` | `MachineTimestamp?` | The moment the budget runs out |
| `CancellationToken` | `CancellationToken?` | The token that fires then |

Both members are null when no budget bounds the request. `GetRemainingMilliseconds()` on `Deadline`
gives the time left. `MachineTimestamp`, in `Hardened.Shared.Runtime.Diagnostics`, reads the
machine's monotonic clock.

This handler goes in the first example's `TodoController`, with
`using Hardened.Requests.Abstract.Timeouts;` added to the file:

```csharp
[Get("/budget")]
[Timeout(Milliseconds = 2000)]
public string Budget(IRequestDeadline deadline) =>
    deadline.Deadline is { } until
        ? $"{until.GetRemainingMilliseconds():F0} ms left"
        : "no budget";
```

```http
GET /todos/budget

HTTP/1.1 200 OK
Content-Type: application/json

"2000 ms left"
```

The number is the time left when the handler read it. It varies by a millisecond or so. The budget
starts when the timeout filter runs, not when the request arrives.

Inside the budget, `IRequestDeadline.CancellationToken`, the handler's `CancellationToken`
parameter and `IExecutionContext.CancellationToken` are the same token. The budget's token is
linked to the request's own token, so a client that disconnects cancels it too. [Limits](#limits)
covers that case.

`[Timeout(Deadline = false)]` makes both members read null. The budget stays, and its token still
fires. The same handler with `[Timeout(Milliseconds = 2000, Deadline = false)]` answers:

```http
GET /todos/budget

HTTP/1.1 200 OK
Content-Type: application/json

"no budget"
```

## Budgets in a contract

In an OpenAPI document, `x-hardened-timeout` on an operation declares its budget in milliseconds.
In a project from `dotnet new hardened-web -n Todos --contract openapi`, the contract is
`src/Todos/contracts/todos.yaml`:

```yaml
  /todos:
    get:
      tags:
        - Todos
      operationId: listTodos
      summary: Every todo.
      x-hardened-timeout: 2000
```

`x-hardened-timeout` can also be an object with `milliseconds`, `status` and `retryAfterSeconds`.
`getTodo` in the same file uses the object form:

```yaml
      operationId: getTodo
      summary: One todo by id.
      x-hardened-timeout:
        milliseconds: 500
        status: 503
        retryAfterSeconds: 30
```

In a Smithy model, `@timeout` on an operation declares its budget. The model names the trait with
`use hardened.api#timeout`. `@timeout` takes `milliseconds`, which is required, and `status` and
`retryAfterSeconds`. In `src/Todos/contracts/todos.smithy` of a project from `--contract smithy`,
the `use` line goes after the `namespace` line:

```smithy
use hardened.api#timeout

@documentation("Every todo.")
@http(method: "GET", uri: "/todos", code: 200)
@timeout(milliseconds: 2000)
@readonly
operation ListTodos {
```

The build adds the trait's definition, `hardened.smithy`, to the model. A committed AST needs that
file passed to the Smithy CLI. [Generating from Smithy](/guide/smithy) covers the command.

In both an OpenAPI document and a Smithy model, `status` defaults to 504 and `retryAfterSeconds` to
0, as on `[Timeout]`. An `x-hardened-timeout` object without `milliseconds` is
[a budget of zero](#a-budget-of-zero-or-less).

The generated handler carries the budget as a `[Timeout]`. A contract cannot set `Deadline`. The
generated `[Timeout]` keeps its default, `true`.

A budget in the contract is the operation's budget. `[Timeout]` on the implementation's method or
class does not change it. A budget in the contract also beats `[assembly: Timeout]` and the
application default.

`[Timeout]` on the implementation bounds an operation that the contract gives no budget. When the
implementation's class and method both declare one, the class's budget applies.

[Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) cover the
rest of a contract.

## The token in a contract-first handler

`HardenedBindCancellationToken` set to `true` adds a `CancellationToken cancellationToken`
parameter, last, to every method of every generated service interface. The property is `false` by
default. It works the same for an OpenAPI document and a Smithy model. It goes in
`src/Todos/Todos.csproj`:

```xml
<PropertyGroup>
  <HardenedBindCancellationToken>true</HardenedBindCancellationToken>
</PropertyGroup>
```

The property takes effect on the next build. In the OpenAPI project, the generated `ITodosService`
then declares these methods:

| Method | Parameters |
|---|---|
| `ListTodos` | `CancellationToken cancellationToken` |
| `GetTodo` | `int id, CancellationToken cancellationToken` |
| `CreateTodo` | `NewTodo body, CancellationToken cancellationToken` |
| `RemoveTodo` | `int id, CancellationToken cancellationToken` |

With the property on, the implementation stops compiling. The build reports `CS0535` for each
method until the method takes the token:

```console
src/Todos/TodoService.cs(28,46): error CS0535: 'TodoService' does not implement interface member 'ITodosService.CreateTodo(NewTodo, CancellationToken)'
src/Todos/TodoService.cs(28,46): error CS0535: 'TodoService' does not implement interface member 'ITodosService.GetTodo(int, CancellationToken)'
src/Todos/TodoService.cs(28,46): error CS0535: 'TodoService' does not implement interface member 'ITodosService.ListTodos(CancellationToken)'
src/Todos/TodoService.cs(28,46): error CS0535: 'TodoService' does not implement interface member 'ITodosService.RemoveTodo(int, CancellationToken)'
```

In the OpenAPI project's `src/Todos/TodoService.cs`, `ListTodos` takes the token. The other three
methods take `CancellationToken cancellationToken` last in the same way:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Todos.Models;
using Todos.Services;

namespace Todos;

[Handler]
public class TodoService(ITodoStore store) : ITodosService
{
    public async Task<List<Todo>> ListTodos(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        return (await store.All()).ToList();
    }
}
```

With `x-hardened-timeout: 2000` on `listTodos`, the request is answered after 2 seconds. The Smithy
project, with the model above, answers the same:

```http
GET /todos

HTTP/1.1 504 Gateway Timeout
Content-Type: application/json

{"type":"GatewayTimeout","message":"The server did not finish this request in time.","details":""}
```

The handler receives the budget's token when a budget applies, and the request's own token
otherwise. A contract-first handler takes `IRequestDeadline` through its constructor.

## What the budget covers

The timeout filter runs at `FilterOrder.BeforeSerialization`, 6500.
[The execution pipeline](/guide/execution-pipeline) covers the order of the filters. These stages
run inside and outside the budget:

| Inside the budget | Outside the budget |
|---|---|
| Reading and binding the request | Authentication |
| Validation | Rate limiting |
| Authorization that reads bound parameters | Authorization over grants alone |
| `[Retry]` and every attempt | Conditional requests |
| The handler | Compression |
| Writing the response | The response cache |

One budget covers every `[Retry]` attempt. When the budget runs out, no further attempt starts. The
caller gets the last attempt's failure.

## A streamed response

When the budget runs out after a stream's first item, the stream ends. The client keeps the items
already sent. The response keeps its 200 status. This handler goes in the first example's
`TodoController`, with `using System.Runtime.CompilerServices;` added to the file:

```csharp
[Get("/feed")]
[Timeout(Milliseconds = 2000)]
public async IAsyncEnumerable<Todo> Feed(
    ITodoStore store,
    [EnumeratorCancellation] CancellationToken cancellationToken
)
{
    foreach (var todo in await store.All())
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);

        yield return todo;
    }
}
```

The template's two todos would take 3 seconds. The first item arrives after 1.5 seconds, and the
response ends at 2 seconds:

```http
GET /todos/feed

HTTP/1.1 200 OK
Content-Type: application/x-ndjson

{"id":1,"title":"Read the generated code","done":true}
```

The end of the stream is logged at `Error` as a failed request, not with the timeout `Warning` line.
The stream ends at the deadline also when the handler does not pass the token on. When the budget
runs out before the first item, the caller gets 500 with an empty body.
[Streaming responses](/guide/streaming) covers a client that disconnects from a stream.

## A budget of zero or less

A budget of zero gives these results:

| Declaration | Result |
|---|---|
| `[Timeout(Milliseconds = 0)]` on a method or a class | Build error `HRDW006` |
| `[assembly: Timeout(Milliseconds = 0)]` | Build error `HRDW006` for each handler it covers |
| `[RequestTimeouts(0)]` | The build succeeds. Every request answers 500, and the log names the handler |
| A convention returning a budget of zero | Every request to the handlers it covers answers 500, and the log names the convention |
| `x-hardened-timeout: 0` | Build error `HOAT002` |
| `@timeout(milliseconds: 0)` | Warning `HSMT006`, and the operation has no budget |

A negative budget gives the same result as zero. `[Timeout(Milliseconds = 0)]` on the first
example's `Export` fails the build with this error:

```console
CSC : error HRDW006: 'TodoController.Export' is bounded by a [Timeout] declaring 0 milliseconds, on the operation, its class or its assembly. A budget has to be greater than zero; a handler that should not be bounded declares no timeout instead. The runtime refuses this on the first request, and the document would publish x-hardened-timeout: 0.
```

[Diagnostics](/reference/diagnostics) lists every code.

## Hosts and tests

Every host enforces a budget: Kestrel, ASP.NET Core, AWS Lambda, Google Cloud Run, Google Cloud
Functions and Azure Functions. A budget also fires in the in-process test host.

## Limits

A handler that blocks a thread, or awaits work without passing the token, runs to completion. After
such a handler returns, its JSON response is sent late with its own status, 200. When such a
handler returns a `text/plain` response, the caller gets 500 with an empty body. That request is
logged at `Error` as a failed request.

A client that disconnects from a bounded handler cancels its token as the deadline would. The
request is logged with the same `Warning` line, "did not finish inside its ... ms budget".
`RequestTimedOut` does not count it.

## Next

| Page | Covers |
|---|---|
| [The execution pipeline](/guide/execution-pipeline) | The order of the filters around the timeout filter |
| [The OpenAPI document](/guide/openapi-document) | The 504 and the `x-hardened-timeout` that a declared budget publishes |
| [Generating from OpenAPI](/guide/openapi) | Declaring the rest of an OpenAPI contract |
| [Streaming responses](/guide/streaming) | Streamed responses and their cancellation |
| [Rate limiting](/guide/rate-limiting) | The other bound on an operation |
