# Content negotiation

The client says what it wants and the pipeline serves it. A handler returns a value and says
nothing about media types:

```csharp
[Get("/hello")]
public string Hello() => "Hello, World!";
```

| Request | Body |
|---|---|
| `Accept: text/plain` | `Hello, World!` |
| `Accept: application/json` | `"Hello, World!"` |
| `Accept: */*`, or no header | `"Hello, World!"` |

The same handler answers a browser with a rendered page when a [view](/guide/templates) exists
for its model, and a CSV client with CSV when a [serializer](#writing-a-serializer) for it is
registered.

## How a serializer is chosen

`ISerializationLocatorService` resolves every response in three tiers.

**A committed content type.** If `Response.ContentType` is already set by the time the response
is serialized, the client does not get to overrule it. That is what
[`[RawResponse]`](#forcing-a-content-type) does. If nothing registered can write the committed
type, that is an error rather than a fallback.

**Negotiation.** Otherwise the `Accept` header is parsed into the media types the client will
take, most preferred first, and each is offered to the serializers in turn:

```
for each media type the client asked for, in preference order
    for each serializer, in Order
        if it can produce that media type for this response, use it
```

The client's preferences are the outer loop, so the client's ranking decides. A request for
`application/json,text/html;q=0.9` against a route that has a view is answered with JSON.

**The default.** If nothing can produce anything the client asked for, the serializer marked
`IsDefaultSerializer` answers.

## What `Accept` means here

The header is split on commas, and everything after a `;` is discarded, `q` included. Preference
comes from the order media types are listed in, so `text/html;q=0.5, application/json;q=0.9`
resolves to `text/html`.

| `Accept` | Matches |
|---|---|
| `application/json` | that type exactly |
| `application/*` | any `application/…` serializer |
| `*/*` | anything |
| absent | anything |

A missing header and `*/*` mean the same thing: every serializer qualifies and `Order` decides.

## Order

`Order` breaks ties between serializers that all satisfy the same preference. Lower is asked
first. The values are `ResponseSerializerOrder`, and `RequestDeserializerOrder` mirrors it:

| Value | Used by |
|---|---|
| `Template` (-1000) | Rendered [views](/guide/templates). Ahead of everything, because a response naming a view is asking for that view |
| `Specialized` (-100) | A serializer for one specific media type |
| `Normal` (0) | The JSON serializers |
| `Deferred` (1000) | Raw string, byte and stream output |

The values are spaced so a serializer can be slotted between two of them without renumbering.
`Order` decides who is asked first. `IsDefaultSerializer` decides who answers when nobody claims
the response at all.

## Declaring what an operation produces

`[SupportedContentTypes]` states the media types one operation can produce, in preference order:

```csharp
[Get("/reports/shelf")]
[SupportedContentTypes("text/plain", "text/csv")]
public string Shelf() => ...;
```

This is the hand-written half of what a description states with the `content:` keys of its success
response. Both reach the same model, so an application written in code and one generated from a
document negotiate the same way.

Order is the server's preference, and it decides what `Accept: */*` — or a request with no `Accept`
at all — is answered with. A client that names types explicitly gets its own preference order
honoured instead.

It is not `[RawResponse]`, which is a different thing. That assigns the content type before the
handler runs and takes the response out of negotiation entirely: "this *is* a PDF". This says what
the operation is able to produce and lets the client choose among them.

## Asking for something outside that set

`[ContentNegotiation]` decides what the service answers a client that asked for a media type no
operation produces:

```csharp
[HardenedModule]
[KestrelRuntime]
[ContentNegotiation(ContentNegotiationMode.Lenient)]
public partial class Application;
```

| Mode | Answers |
|---|---|
| `Strict` | `406 Not Acceptable`. The default |
| `Lenient` | Serializes with the default serializer anyway |

`Strict` is the answer HTTP defines: a client that names media types and shares none with the
operation has asked for something that does not exist. Nothing about it is the API author's to
describe, unlike a 404, so it is not derived from the document and does not need declaring.

`Lenient` is for an application that would rather answer something than nothing — or one migrating,
whose clients send `Accept: application/json` at operations that never produced JSON and were
answered with the declared string wrapped in quotes.

It goes on the entry point, and it is one answer for the whole service. Deliberately not per
operation: a policy that has to be repeated is one that ends up applied unevenly, and a single
operation quietly negotiating while every other refuses is worse than either answer applied
consistently — the omission would be invisible.

A description says the same thing with `x-hardened-content-negotiation` at its root.

## Strings, bytes and streams

A handler returning `string`, `byte[]` or `Stream` is written straight to the body rather than
structured, but only when asked for. A bare string is offered as `text/plain`, not forced to it.
`RawResponseSerializer` is ordered `Deferred`, behind JSON, so a client that expressed no
preference gets JSON, as the table at the top shows.

`byte[]` and `Stream` are not offered under negotiation at all. They need a committed content
type.

## Forcing a content type

`[RawResponse]` commits the response, so the client cannot negotiate it away:

```csharp
[Get("/report.csv")]
[RawResponse("text/csv")]
public string Report() => _reports.Csv();
```

```csharp
[Get("/invoices/{id}")]
[RawResponse("application/pdf")]
public Stream Invoice(string id) => _invoices.Render(id);
```

A handler can do the same per request by assigning `Response.ContentType` before returning:

```csharp
public byte[] Export(IExecutionContext context, string format) {
    context.Response.ContentType = format == "csv" ? "text/csv" : "application/vnd.ms-excel";

    return _exports.Build(format);
}
```

## Writing a serializer

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Serializer;

[SingletonService(Using = RegistrationType.Add)]
public class CsvResponseSerializer : IResponseSerializer {
    public bool IsDefaultSerializer => false;

    public int Order => (int)ResponseSerializerOrder.Specialized;

    public bool CanProduce(string mediaType, IExecutionContext context) =>
        MediaType.Matches(mediaType, "text/csv") &&
        context.Response.ResponseValue is IEnumerable<object>;

    public Task SerializeResponse(IExecutionContext context) { /* … */ }
}
```

Two things to get right. Use `MediaType.Matches` rather than comparing the string, because that
is where wildcard handling lives, so `*/*` and a missing header resolve correctly. And register
with `Add`, never `Try`: `RegistrationType.Try` emits `TryAddSingleton`, which on an interface
resolved as a set means "do not register if anyone else already did", so a serializer registered
that way never enters the container.

`CanProduce` answers two questions at once: does this serializer emit that media type, and can it
handle this particular response value.

## Handlers that declare an output

None of this applies to a handler carrying [`[Output<T>]`](/guide/templates). The output either
answers what the client asked for, or the request gets `406 Not Acceptable`. No serializer is
consulted and there is no fallback, so adding `[Output<T>]` to a handler can never widen what it
discloses. To serve both representations from one handler, do not declare an output: return the
model and let negotiation choose.

## Request bodies

Deserialization is simpler. `IRequestDeserializer.CanProcessContext` returns a bool against the
request's `Content-Type`, which is a single stated value with nothing to rank.

A `Content-Type` no deserializer claims falls back to the default deserializer, which is JSON, so
a `text/plain` body is parsed as JSON and refused 400 with the parser's message rather than 415.
The 415 is answered for a `Content-Encoding` nothing can decode.

## Next

- [JSON serialization](/guide/json): the serializer at `Normal`
- [Views](/guide/templates): the serializer at `Template`
- [Streaming responses](/guide/streaming): NDJSON and server-sent events
