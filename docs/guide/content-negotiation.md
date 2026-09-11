# Content negotiation

An operation declares what it produces. JSON is the default, so most operations declare nothing:

```csharp
[Get("/orders/{id}")]
public Order Read(int id) => ...;          // application/json

[Get("/reports/{id}")]
[Produces("application/pdf")]
public byte[] Report(int id) => ...;       // application/pdf, written as-is

[Get("/reports/shelf")]
[Produces("text/plain", "text/csv")]
public string Shelf() => ...;              // the client chooses
```

The declaration is resolved when the application starts. The serializer for it is found once, as
each handler's pipeline is composed, and held there. A request costs the call: no `Accept` header to
parse, no serializer set to search, no container resolve.

Only the third handler above negotiates, because only it offers a choice.

## What a handler returns decides how it is written

Three shapes, and the return type picks between them before any media type does.

| Return type | Written by |
|---|---|
| `byte[]`, `Stream` | The handler. The bytes go out unchanged under the declared media type |
| `IAsyncEnumerable<T>` | The [streaming](/guide/streaming) filter, one item at a time |
| anything else | A serializer, chosen by the declared media type |

**Returning `byte[]` or `Stream` means the handler controls its own serialization.** No serializer
is consulted, on any request, whatever `Accept` said. Those handlers must carry `[Produces]`: bytes
have no media type anyone could infer, and a handler that declares none is build error
[`HRDR011`](/reference/diagnostics).

A `string` is not one of them. It has a JSON reading — a quoted string — and that is what a handler
declaring nothing answers with. Declaring a media type is what sends it to the pass-through writer:

```csharp
[Get("/hello")]
public string Hello() => "Hello, World!";                  // "Hello, World!"

[Get("/hello.txt")]
[Produces("text/plain")]
public string HelloText() => "Hello, World!";              //  Hello, World!
```

## Declaring what an operation produces

`[Produces]` takes one media type or several, and the count is the difference.

**One is a declaration.** The operation returns that type and does not read `Accept` at all, which
is what most JSON APIs do and what HTTP permits. A service that would rather refuse a client asking
for something else opts into that with [`[ContentNegotiation]`](#asking-for-something-outside-that-set).

**Several is a set, and negotiates.** The client's own preference order decides within it. The
first value is what a client expressing no preference is answered with — `Accept: */*`, or no
`Accept` at all — because the first representation a document lists is the one it leads with.

It is the hand-written half of what a description states with the `content:` keys of its success
response. Both reach the same model, so an application written in code and one generated from a
document behave the same way.

`[Produces]` is a statement about the response only. An operation that accepts JSON and returns CSV
is ordinary, and the inbound `Content-Type` is read by a [different mechanism](#request-bodies).

### Where a declaration comes from

Four places, nearest to the handler first. Nothing is combined: the nearest declaration is the
answer and the rest are fallbacks.

| Rung | Written as |
|---|---|
| The operation | `[Produces("text/csv")]` on the method |
| Its class | `[Produces("text/csv")]` on the controller |
| The handler's assembly | `[assembly: Produces("text/csv")]` |
| The application | `services.AddSingleton(new ResponseContentTypeDefault("text/csv"))` |

The bottom rung is a registration rather than an attribute, which is how a serializer package makes
itself the default for a whole service: it registers its `IResponseSerializer` and this together, and
importing the module is the whole of the configuration.

**The assembly beats the application's default.** A library writing `[assembly: Produces]` is saying
something specific about its own handlers; a registered default is a blanket fallback for handlers
that said nothing. Read the other way round, a host would silently change what a library declared.

## Aliases

Two attributes are `[Produces]` under other names.

`[ServerSentEvents]` is `[Produces("text/event-stream")]`, and reads better on a stream. The framing
follows the declared media type, so either spelling frames a stream as events.

`[RawResponse]` is deprecated. It stated two facts — the media type, and that the return value is
already bytes — and the second is now the return type's job. `[RawResponse("text/csv")]` is
`[Produces("text/csv")]`.

## When negotiation still runs

An operation that declares two or more media types reads the `Accept` header, and so does one that
declares a single type under `ContentNegotiationMode.Strict`, where a mismatch is a refusal rather
than an answer.

The client's preferences are the outer loop, so the client's ranking decides rather than the
server's. The header is walked in place and nothing is allocated: it is split on commas, everything
after a `;` is discarded, `q` included, and preference comes from the order media types are listed
in — so `text/html;q=0.5, application/json;q=0.9` resolves to `text/html`.

| `Accept` | Matches |
|---|---|
| `application/json` | that type exactly |
| `application/*` | any `application/…` serializer |
| `*/*` | anything |
| absent | anything |

A missing header and `*/*` mean the same thing: the client will take whatever the operation leads
with.

## Two serializers, one media type

A serializer declares the media type it writes, and **the last registration under a media type is
the one that answers**. A module the application imports is applied after the framework module it
depends on, so importing a package that replaces JSON is enough to be sure it is used.

There used to be an `Order` as well. It existed because reverse-registration order within a module is
decided by how implementation type names sort, which an application cannot steer. A declared media
type makes the contest a lookup, so there is nothing left to adjudicate.

`IsDefaultSerializer` is a different question, and stays: it decides who writes a response no
operation declared a media type for.

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

## Choosing a content type per request

A handler can assign `Response.ContentType` before returning, which overrules whatever its pipeline
resolved. That is the one decision the build cannot make for it:

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

    public string ContentType => "text/csv";

    public Task SerializeResponse(IExecutionContext context) { /* … */ }
}
```

Three members, and the media type is the whole of how it is found. A handler carrying
`[Produces("text/csv")]` is bound to it as its pipeline is composed.

Register with `Add`, never `Try`. `RegistrationType.Try` emits `TryAddSingleton`, which on an
interface resolved as a set means "do not register if anyone else already did", so a serializer
registered that way never enters the container.

There is a `CanProduce` as well, defaulted in terms of `ContentType`, and most serializers should
leave it alone. Override it only where the question is about the response value rather than the
media type — `RawResponseSerializer` writes bytes it is handed and nothing else.

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

- [JSON serialization](/guide/json): the default serializer
- [Views](/guide/templates): a handler that writes its own response
- [Streaming responses](/guide/streaming): NDJSON and server-sent events
