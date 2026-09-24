# Streaming responses

A handler that returns `IAsyncEnumerable<T>` streams its response. Each item is written and flushed
as the handler yields it, so a client reads the first item while the handler is still producing the
rest.

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoFeedController
{
    [Get("/feed")]
    public async IAsyncEnumerable<Todo> Feed(ITodoStore store)
    {
        foreach (var todo in await store.All())
        {
            yield return todo;
        }
    }
}
```

```http
GET /todos/feed

HTTP/1.1 200 OK
Content-Type: application/x-ndjson

{"id":1,"title":"Read the generated code","done":true}
{"id":2,"title":"Add an endpoint","done":false}
```

By default, each item is one line of JSON. The response's `Content-Type` is `application/x-ndjson`
(newline-delimited JSON). NDJSON needs no attribute and no registration.

The code above is the whole of `src/Todos/TodoFeedController.cs`, a file added to the library
project of `dotnet new hardened-web -n Todos`. The library's module carries `[BasePath("/todos")]`.
The other handlers on this page are methods added to this class, apart from the Smithy example. Each
block shows the `using` lines it adds to its file.

## NDJSON and server-sent events

`[ServerSentEvents]` on the handler sends the items as server-sent events, with
`Content-Type: text/event-stream`. The attribute is in `Hardened.Web.Runtime.Attributes`. It goes on
a method.

```csharp
[Get("/events")]
[ServerSentEvents]
public async IAsyncEnumerable<Todo> Events(ITodoStore store)
{
    foreach (var todo in await store.All())
    {
        yield return todo;
    }
}
```

```http
GET /todos/events

HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

data: {"id":1,"title":"Read the generated code","done":true}

data: {"id":2,"title":"Add an endpoint","done":false}
```

NDJSON and server-sent events write a stream this way:

| | NDJSON | Server-sent events |
|---|---|---|
| Declared with | Nothing | `[ServerSentEvents]`, or `[Produces("text/event-stream")]` on the method or its class |
| `Content-Type` | `application/x-ndjson` | `text/event-stream` |
| One item | The item's JSON and a newline | `data: `, the item's JSON, and an empty line |
| After the last item | One empty line | Nothing more |
| A stream with no items | A body of one newline | The comment line `:` and an empty line |
| A quiet spell | Nothing | The comment line `: keep-alive` and an empty line |
| Headers added | None | `Cache-Control: no-cache`, `X-Accel-Buffering: no` |

`[Produces("text/event-stream")]` frames a stream the same way as `[ServerSentEvents]`. On a class,
it frames every stream in the class. A stream is framed as events whenever `text/event-stream` is
among its declared media types, as in `[Produces("application/json", "text/event-stream")]`. Every
other stream is NDJSON under `application/x-ndjson`, whatever other media type `[Produces]` names.

A stream's framing is fixed when the application is built. The request's `Accept` header does not
change it. A stream is never answered 406. [Content negotiation](/guide/content-negotiation) covers
`Accept` and `[Produces]`.

Each item is written as JSON with the application's JSON settings, in both framings. In an
application whose other responses are MessagePack, the items are still JSON.
[JSON serialization](/guide/json) covers the settings. A `string` item is written as a JSON string:
`"alpha"`.

`[ServerSentEvents]` on a handler that does not return `IAsyncEnumerable<T>` fails the build with
`HRDW004`. So does `[Produces("text/event-stream")]` on such a handler or on its class. A handler
that returns `Stream`, `byte[]` or `string` to write its own bytes fails the same way, so a handler
cannot declare `text/event-stream` and write the events itself.

## Event fields

Yield `SseItem<T>` in place of `T` to set an event's fields. The type is in
`Hardened.Requests.Abstract.Serializer`.

```csharp
using Hardened.Requests.Abstract.Serializer;

[Get("/changes")]
[ServerSentEvents]
public async IAsyncEnumerable<SseItem<Todo>> Changes(ITodoStore store)
{
    foreach (var todo in await store.All())
    {
        yield return new SseItem<Todo>(todo, Id: todo.Id.ToString(), Event: "todo");
    }
}
```

```http
GET /todos/changes

HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

id: 1
event: todo
data: {"id":1,"title":"Read the generated code","done":true}

id: 2
event: todo
data: {"id":2,"title":"Add an endpoint","done":false}
```

`SseItem<T>` has four parameters:

| Parameter | Type | Written as | What a client does with it |
|---|---|---|---|
| `Data` | `T` | `data:` and the item's JSON | Reads it as the event's payload |
| `Id` | `string?` | `id:` | Sends the last one back as `Last-Event-ID` when it reconnects |
| `Event` | `string?` | `event:` | Delivers the event to the listener for that name |
| `Retry` | `int?` | `retry:` | Waits that many milliseconds before it reconnects |

`data:` carries `Data` alone. The other fields are written on their own lines before it, in the
order `id:`, `event:`, `retry:`. A field whose value is null or empty is not written. An `Id` or an
`Event` that contains a line break is not written either.

An `EventSource` delivers an event that has an `event:` field to the listener registered for that
name. It delivers an event without one to `onmessage`.

On an NDJSON stream, `SseItem<T>` is serialized whole. Each line is the record, such as
`{"data":{"id":1,"title":"Read the generated code","done":true},"id":"1","event":null,"retry":null}`.
In both framings, the OpenAPI document describes the items as `T`, not as `SseItem<T>`.

## Resuming after a reconnect

An `EventSource` reconnects on its own after a stream ends. It sends the `id` of the last event it
received in the `Last-Event-ID` header. A handler binds the header with
`[FromHeader(KnownHeaders.LastEventId)] string? lastEventId`. `KnownHeaders` is in
`Hardened.Requests.Abstract.Headers`. [Parameter binding](/guide/parameter-binding) covers
`[FromHeader]`.

The handler decides what to send again from the id. This version of `Changes` replaces the one
above. It skips the todos up to the id.

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;

[Get("/changes")]
[ServerSentEvents]
public async IAsyncEnumerable<SseItem<Todo>> Changes(
    ITodoStore store,
    [FromHeader(KnownHeaders.LastEventId)] string? lastEventId,
    IExecutionContext context
)
{
    var after = int.TryParse(lastEventId, out var id) ? id : 0;

    var todos = (await store.All()).Where(todo => todo.Id > after).ToList();

    if (todos.Count == 0)
    {
        context.Response.Status = 204;

        yield break;
    }

    foreach (var todo in todos)
    {
        yield return new SseItem<Todo>(todo, Id: todo.Id.ToString(), Event: "todo");
    }
}
```

```http
GET /todos/changes
Last-Event-ID: 1

HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

id: 2
event: todo
data: {"id":2,"title":"Add an endpoint","done":false}
```

Without the header, the handler answers as the first version of `Changes` does. A stream that sets
no ids is sent from its start on every reconnect. The OpenAPI document lists `Last-Event-ID` as an
optional header parameter of the operation.

## Ending a subscription

A handler ends a subscription by setting `context.Response.Status = 204` before its first item and
yielding nothing. The `Changes` handler above does this when no todo is newer than the id.
`IExecutionContext` is in `Hardened.Requests.Abstract.Execution`.

```http
GET /todos/changes
Last-Event-ID: 2

HTTP/1.1 204 No Content
```

The 204 goes out with no body and no `Content-Type`. An `EventSource` does not reconnect after
a 204. It does not reconnect after a refusal either, such as a 404 answered before the first item.
An NDJSON stream sends a 204 set before its first item the same way. A 304 is treated like a 204.
The OpenAPI document does not list a 204 that the handler sets this way.

::: warning
A 204 set after the first item, or after a heartbeat, does not end the subscription on a host that
streams. On Kestrel and ASP.NET Core, the client keeps the 200 and the items already sent. An
`EventSource` reconnects. On those hosts, the assignment throws `InvalidOperationException` inside
the handler. The request is logged as failed.
:::

## Heartbeats and headers

An event stream that is quiet for 15 seconds gets a heartbeat: the comment line `: keep-alive` and
an empty line. It gets another after each further 15 quiet seconds. An `EventSource` delivers no
event for a heartbeat. The connection stays open. An NDJSON stream never gets a heartbeat.

The status line and the headers go out with the first item or the first heartbeat, not when the
handler is called.

`services.ConfigureStreaming` sets the interval. `TimeSpan.Zero` turns heartbeats off. The method is
in `Hardened.Requests.Runtime.Streaming`. This is the template's `ConfigureServices` method in
`src/Todos/TodosLibrary.cs`, with the `ConfigureStreaming` line added:

```csharp
using Hardened.Requests.Runtime.Streaming;

public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

    services.ConfigureStreaming(streaming => streaming.HeartbeatInterval = TimeSpan.FromSeconds(5));
}
```

Every event stream carries `Cache-Control: no-cache` and `X-Accel-Buffering: no`. A value that the
handler sets for either header before the first item stays. An NDJSON stream gets neither header.

## Refusals and failures

Before the first item, a refusal or a failure is answered with its own status and a JSON body, as on
any route. A handler declares a stream's refusal with `[Throws<T>]` and throws it with
`AsException()` before the first item. [Declared responses](/guide/responses) covers both.

```csharp
using Hardened.Web.Runtime.Responses;

[Get("/{id}/changes")]
[ServerSentEvents]
[Throws<NotFound>]
public async IAsyncEnumerable<Todo> TodoChanges(ITodoStore store, int id)
{
    var todo = await store.Find(id);

    if (todo is null)
    {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    yield return todo;
}
```

```http
GET /todos/99/changes

HTTP/1.1 404 Not Found
Content-Type: application/json

{"resource":"todo","detail":"No todo has id 99.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

The OpenAPI document publishes a stream's refusals as `application/json`.

After the first item, an exception ends the stream. The items already sent stay with the client. The
response ends as a finished stream does, with its 200. The exception is logged as a failed request.
An `EventSource` reconnects after such a failure, because the stream ended.

| The failure comes | Status | Body | An `EventSource` |
|---|---|---|---|
| Before the first item: a refusal, or an exception | The failure's own, such as 400, 401, 404 or 500 | The JSON error body | Does not reconnect |
| After the first item | 200 | The items sent before the failure, then a normal end | Reconnects |

`[Retry]` retries the call to the handler. The body of an `async` iterator runs later, while the
stream is written, so `[Retry]` does not retry a failure inside it, before or after the first item.
A handler that is not an iterator, and throws before it returns the sequence, is retried.

## Cancellation

The request's `CancellationToken` is cancelled when the client disconnects. A handler takes it as a
parameter. [Parameter binding](/guide/parameter-binding) covers the parameter. On an `async`
iterator, mark the parameter `[EnumeratorCancellation]`, from `System.Runtime.CompilerServices`.
Without it, the compiler warns `CS8425`. The parameter still binds.

```csharp
using System.Runtime.CompilerServices;

[Get("/watch")]
[ServerSentEvents]
public async IAsyncEnumerable<Todo> Watch(
    ITodoStore store,
    [EnumeratorCancellation] CancellationToken cancellationToken
)
{
    while (true)
    {
        foreach (var todo in await store.All())
        {
            yield return todo;
        }

        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
    }
}
```

The exchange shows the first 12 seconds, with the 5-second heartbeat configured above. The stream
does not end.

```http
GET /todos/watch

HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

data: {"id":1,"title":"Read the generated code","done":true}

data: {"id":2,"title":"Add an endpoint","done":false}

: keep-alive

: keep-alive
```

A handler that passes the token to the work it awaits stops when the client disconnects. A handler
that does not pass it on runs until it yields its next item. The write then fails, and the stream
ends.

## Compression

An NDJSON stream is compressed when the application compresses responses and the request accepts
the coding. Each item can still be read as it arrives. [Compression](/guide/compression) covers
turning it on. An event stream is not compressed.

## The OpenAPI document

The OpenAPI document lists the media type of a stream's framing under the success status. Under
the media type, `schema` is an array of the item, and `itemSchema` is the item. The item is `T` for
both `IAsyncEnumerable<T>` and `IAsyncEnumerable<SseItem<T>>`. This is the operation for the first
example, in the served `/openapi.json`:

```json
{
  "/todos/feed": {
    "get": {
      "tags": [
        "TodoFeed"
      ],
      "operationId": "feed",
      "responses": {
        "200": {
          "description": "OK",
          "content": {
            "application/x-ndjson": {
              "schema": {
                "type": "array",
                "items": {
                  "$ref": "#/components/schemas/Todo"
                }
              },
              "itemSchema": {
                "$ref": "#/components/schemas/Todo"
              }
            }
          }
        }
      }
    }
  }
}
```

Below OpenAPI 3.2, the document leaves out `itemSchema`. At those versions, the build warns
`HRDOA002` for each handler that streams. [The OpenAPI document](/guide/openapi-document) covers the
version. [Generated clients](/guide/clients) covers what Kiota and Refitter generate for a stream.

## Streams from a contract

In an OpenAPI 3.2 contract, a response with `itemSchema` generates a method that returns
`IAsyncEnumerable<T>`. [Generating from OpenAPI](/guide/openapi) covers the contract. The framing
follows the media type the contract names. `text/event-stream` is server-sent events. Every other
media type is NDJSON, sent and published as `application/x-ndjson`.

A Smithy `@streaming` union bound as the output's `@httpPayload` generates a method that returns an
`IAsyncEnumerable` of the union. Its items are sent as server-sent events.
[Generating from Smithy](/guide/smithy) covers the model.

| The contract declares | Sent as | `Content-Type`, and the media type the served document lists |
|---|---|---|
| OpenAPI `itemSchema` under `text/event-stream` | Server-sent events | `text/event-stream` |
| OpenAPI `itemSchema` under `application/x-ndjson` | NDJSON | `application/x-ndjson` |
| OpenAPI `itemSchema` under another media type, such as `application/jsonl` | NDJSON | `application/x-ndjson` |
| A Smithy `@streaming` union as the output's `@httpPayload` | Server-sent events, with `event:` naming the member | `text/event-stream` |

The model below is added to `src/Todos/contracts/todos.smithy` in a project from
`dotnet new hardened-web -n Todos --contract smithy`. `StreamChanges` is also added to the service's
`operations` list.

```smithy
structure TodoRemoved {
    @required
    id: Integer
}

@streaming
union TodoChange {
    added: Todo
    removed: TodoRemoved
}

@documentation("What happened to the todos.")
@http(method: "GET", uri: "/todos/changes", code: 200)
@readonly
operation StreamChanges {
    output := {
        @required
        @httpPayload
        changes: TodoChange
    }
}
```

The generated method returns `IAsyncEnumerable<TodoChange>`. Its implementation goes in the
template's `TodoService` class, in `src/Todos/TodoService.cs`, which already has the `using` lines
it needs:

```csharp
public async IAsyncEnumerable<TodoChange> StreamChanges()
{
    foreach (var todo in await store.All())
    {
        yield return todo;
    }

    yield return new TodoRemoved(2);
}
```

```http
GET /todos/changes

HTTP/1.1 200 OK
Content-Type: text/event-stream
Cache-Control: no-cache
X-Accel-Buffering: no

event: added
data: {"id":1,"title":"Read the generated code","done":true}

event: added
data: {"id":2,"title":"Add an endpoint","done":false}

event: removed
data: {"id":2}
```

Each item of a streamed union is one event. `data:` is the member's own JSON, and `event:` is the
member's name in the model. An OpenAPI item that is a `oneOf` is sent the same way, with no `event:`
field. The OpenAPI document publishes a union item as a `oneOf` of its members. An item from a
contract carries no `id:` and no `retry:`.

## Hosts

| Host | The items arrive as they are written |
|---|---|
| Kestrel | Yes |
| ASP.NET Core | Yes |
| Google Cloud Run | Yes |
| Google Cloud Functions | Yes |
| AWS Lambda, with `HARDENED_LAMBDA_RESPONSE_MODE=stream` | Yes |
| AWS Lambda, buffered, which is the default | No. They arrive together when the handler finishes |
| Azure Functions | No. They arrive together when the handler finishes |

A buffered host sends the same framing and headers, all at once.

On Lambda, a response streams only with `HARDENED_LAMBDA_RESPONSE_MODE=stream`, behind a function
URL in `RESPONSE_STREAM` invoke mode. The AWS [Web applications](/aws/lambda-web) page covers the
setting. In that mode, the invocation opens a Lambda response stream at the first byte and writes
the items to it.

The AWS Lambda Test Tool, which the template starts for a local run, cannot run the stream mode. The
AWS [Web applications](/aws/lambda-web) page covers what it does, and the AWS
[Testing](/aws/testing) page covers testing the stream mode in process.

## Limits

A stream cannot carry a `null` item. On Kestrel and ASP.NET Core, a `null` ends the stream after the
items before it. The response ends as a finished one does. On Azure Functions, and on Lambda in
buffered mode, the whole response becomes a 404 whose body still holds the other items.

A `Response<T1..Tn>` whose case is an `IAsyncEnumerable<T>` does not stream. That case goes out as
one JSON array under `application/json`. The OpenAPI document publishes its 200 with no content.

A HEAD request to a stream runs the handler and waits for the stream to end. The response then
carries the whole body's `Content-Length`. A HEAD request to a stream that does not end gets no
answer.

## Next

- [Content negotiation](/guide/content-negotiation): how `Accept` is matched, and what `[Produces]`
  declares
- [The OpenAPI document](/guide/openapi-document): the document the application serves, and its
  version
- [Generated clients](/guide/clients): what a client generated from the document does with a stream
- [Web applications](/aws/lambda-web): setting the Lambda response mode
- [Sending requests](/guide/testing-web): reading a streamed body in a test
