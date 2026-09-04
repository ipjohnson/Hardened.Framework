# Content negotiation

The client says what it wants and the pipeline serves it. A handler returns a model and says nothing
about media types, so one handler can answer a browser with HTML and an API client with JSON from
the same return value.

## How a serializer is chosen

`ISerializationLocatorService` resolves every response in three tiers.

**1. A committed content type.** If `Response.ContentType` is already set by the time the response is
serialised, the client does not get to overrule it. That is what
[`[RawResponse]`](#forcing-a-content-type) does. If nothing registered can write the committed type,
that is an error rather than a fallback.

**2. Negotiation.** Otherwise the `Accept` header is parsed into the media types the client will
take, most preferred first, and each is offered to the serializers in turn:

```
for each media type the client asked for, in preference order
    for each serializer, in Order
        if it can produce that media type for this response, use it
```

The client's preferences are the outer loop, so the client's ranking decides. A request for
`application/json,text/html;q=0.9` against a route that names a view is answered with JSON.

**3. The default.** If nothing can produce anything the client asked for, the serializer marked
`IsDefaultSerializer` answers.

## What `Accept` means here

The header is split on commas, and everything after a `;` is discarded — including `q`. Preference
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

`Order` breaks ties between serializers that all satisfy the same preference. Lower is asked first.
The values are `ResponseSerializerOrder`, and `RequestDeserializerOrder` mirrors it:

| Value | Used by |
|---|---|
| `Template` (-1000) | Rendered [views](/guide/templates). Ahead of everything, because a response naming a view is asking for that view and would otherwise be taken by whichever serializer matched the request's `Accept` |
| `Specialized` (-100) | A serializer for one specific media type |
| `Normal` (0) | The JSON serializers |
| `Deferred` (1000) | Raw string, byte and stream output |

The values are spaced so a serializer can be slotted between two of them without renumbering.
`Deferred` is where `RawResponseSerializer` sits: ahead of JSON it would make every handler
returning a bare string answer `text/plain` to a client sending no `Accept`.

`Order` decides who is *asked* first; `IsDefaultSerializer` decides who answers when nobody claims
the response at all.

## Returning a string

A handler returning `string`, `byte[]` or `Stream` is written straight to the body rather than
structured, but only when asked for:

| Request | `public string Hello() => "Hello, World!"` |
|---|---|
| `Accept: text/plain` | `Hello, World!` |
| `Accept: application/json` | `"Hello, World!"` |
| `Accept: */*`, or no header | `"Hello, World!"` |

A bare string is *offered* as `text/plain`, not forced to it — `RawResponseSerializer` is ordered
`Deferred`, behind JSON, so a client that expressed no preference gets JSON.

`byte[]` and `Stream` are not offered under negotiation at all. They need a committed content type.

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

Two things to get right.

**Use `MediaType.Matches` rather than comparing the string.** It is where wildcard handling lives, so
`*/*` and a missing header resolve correctly.

**Register with `Add`, never `Try`.** `RegistrationType.Try` emits `TryAddSingleton`, which on an
interface resolved as a set means "do not register if anyone else already did". A serializer
registered that way silently never enters the container.

`CanProduce` answers two questions at once: does this serializer emit that media type, and can it
handle this particular response value.

## Handlers that declare an output

None of this applies to a handler carrying [`[Output<T>]`](/guide/templates). The output either
answers what the client asked for, or the request gets `406 Not Acceptable`. No serializer is
consulted and there is no fallback, so adding `[Output<T>]` to a handler can never widen what it
discloses.

To serve both representations from one handler, do not declare an output: return the model and let
negotiation choose a serializer.

## Request bodies

Deserialisation is simpler. `IRequestDeserializer.CanProcessContext` returns a bool against the
request's `Content-Type`, which is a single stated value with nothing to rank.
