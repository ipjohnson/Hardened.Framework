# Content negotiation

The client says what it wants and the pipeline serves it. A handler returns a model and says nothing
about media types — which is what lets one handler answer a browser with HTML and an API client with
JSON, from the same return value.

## How a serializer is chosen

`ISerializationLocatorService` resolves every response in three tiers.

**1. A committed content type.** If `Response.ContentType` is already set by the time the response is
serialised, the response has committed to it and the client does not get to overrule it. That is
what [`[RawResponse]`](#forcing-a-content-type) does. If nothing registered can write the committed
type, that is an error rather than a quiet fallback — the response promised something the
application cannot produce.

**2. Negotiation.** Otherwise the `Accept` header is parsed into the media types the client will
take, most preferred first, and each is offered to the serializers in turn:

```
for each media type the client asked for, in preference order
    for each serializer, in Order
        if it can produce that media type for this response, use it
```

The client's preferences are the outer loop. That is what makes the client's ranking decide rather
than the server's — a request for `application/json,text/html;q=0.9` against a route that names a
view is answered with JSON, because `application/json` is asked about first.

**3. The default.** If nothing can produce anything the client asked for, the serializer marked
`IsDefaultSerializer` answers. A request with an `Accept` nobody satisfies still gets a response.

## What `Accept` means here

The header is split on commas, and everything after a `;` is discarded — including `q`. Preference
comes from the order media types are listed in.

That is how well-formed clients write the header: they list their preferred type first and use `q`
only to restate the order. All three of TechEmpower's benchmark headers do exactly that. A header
that contradicts its own order — `text/html;q=0.5, application/json;q=0.9` — resolves to `text/html`
here. That is a decision rather than an oversight; honouring `q` means sorting, and nothing observed
in practice needs it.

Wildcards work as you would expect:

| `Accept` | Matches |
|---|---|
| `application/json` | that type exactly |
| `application/*` | any `application/…` serializer |
| `*/*` | anything |
| absent | anything |

A missing header and `*/*` mean the same thing: the client expressed no preference, every serializer
qualifies, and `Order` decides. That is the only case where the server's own ranking chooses.

## Order

`Order` breaks ties between serializers that all satisfy the same preference. Lower runs first,
matching `ExecutionFilterOrder`.

| Value | Used by |
|---|---|
| `Template` (-1000) | Rendered views |
| `Specialized` (-100) | A serializer for one specific media type |
| `Normal` (0) | The JSON serializers |
| `Deferred` (1000) | Raw string, byte and stream output |

`Order` and `IsDefaultSerializer` answer different questions. `Order` decides who is *asked* first;
`IsDefaultSerializer` decides who answers when nobody claims the response at all. A specialist
sitting ahead of JSON must not cost JSON its role as the fallback, which is what serves every
request that expressed no preference.

## Returning a string

A handler returning `string`, `byte[]` or `Stream` is written straight to the body rather than
structured. But only when asked for:

| Request | `public string Hello() => "Hello, World!"` |
|---|---|
| `Accept: text/plain` | `Hello, World!` |
| `Accept: application/json` | `"Hello, World!"` |
| `Accept: */*`, or no header | `"Hello, World!"` |

A bare string is *offered* as `text/plain`, not forced to it — `RawResponseSerializer` is ordered
`Deferred`, behind JSON, so a client that expressed no preference still gets JSON. ASP.NET Core makes
the opposite choice and answers `text/plain`; Hardened does not, because it would change what every
existing handler returning a string produces.

`byte[]` and `Stream` are not offered under negotiation at all. There is no media type worth guessing
for them, so they need a committed content type.

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

A handler can do the same per request by assigning `Response.ContentType` before returning, which is
useful when the type depends on the work:

```csharp
public byte[] Export(IExecutionContext context, string format) {
    context.Response.ContentType = format == "csv" ? "text/csv" : "application/vnd.ms-excel";

    return _exports.Build(format);
}
```

Forcing is for responses where there is genuinely one right answer — a PDF is a PDF whatever the
caller's `Accept` says. Everything else should negotiate.

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

**Use `MediaType.Matches` rather than comparing the string.** It is the one place wildcard handling
lives, so `*/*` and a missing header resolve correctly without every serializer reimplementing them.

**Register with `Add`, never `Try`.** `RegistrationType.Try` emits `TryAddSingleton`, which is
first-wins *per service type* — on an interface resolved as a set it means "do not register if
anyone else already did." A serializer registered that way silently never enters the container, and
a no-op registration raises nothing.

`CanProduce` answers two questions at once: does this serializer emit that media type, and can it
handle this particular response value. Answering `false` for a value it cannot write is how a
serializer stays out of the way — a template serializer declines a response that names no view,
however well the media type matches.

## Request bodies

Deserialisation is deliberately simpler. `IRequestDeserializer.CanProcessContext` returns a bool
against the request's `Content-Type`, which is a single stated value with nothing to rank. There is
no negotiation to do, so there is no ranking to express.
