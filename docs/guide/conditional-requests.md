# Conditional requests

A client that polls a resource every ten seconds downloads the same bytes every ten seconds.
`[ConditionalGet]` answers a caller who already holds the response with a 304 and no body.

```csharp
[Get("/rates/{symbol}")]
[ConditionalGet]
public Rate Read(string symbol) => _rates.Latest(symbol);
```

```
GET /rates/EUR
HTTP/1.1 200 OK
ETag: "OybX3FuqNfSKoSm+h1FJqQ=="

GET /rates/EUR
If-None-Match: "OybX3FuqNfSKoSm+h1FJqQ=="
HTTP/1.1 304 Not Modified
ETag: "OybX3FuqNfSKoSm+h1FJqQ=="
```

The attribute goes on an operation or on a class. For every GET handler in the application:

```csharp
[HardenedModule]
[Enable<ConditionalGet>]
[KestrelRuntime]
public partial class Application { }
```

That stands down for any handler carrying `[ConditionalGet]` itself, so explicit beats convention.

**GET handlers only, in both forms.** The routing table sends a HEAD to the GET leaf, and on any
other method the conditional headers mean a 412, which this does not answer. So a class-level
declaration on a controller that also writes installs nothing on the writes.

## Why it is declared and not assumed

Nothing answers a 304 until one of those two declarations is written. A service whose responses are
small and change on every read gets nothing from a 304, and this way it pays nothing for one.

The filter decides on the first write. A response that already carries an `ETag` by then is decided
there and then, either a 304 or the bytes straight through. A response carrying none is held back
and tagged over the bytes as sent once they are all there, which is a buffer and a hash per
response. That is the cost, and it buys bandwidth only. The handler ran. A 304 computed from a hash
of its output saves the transfer and the client's parse, not the work.

It is worth having for a large or frequently polled response, and for a shared cache in front of the
service, which revalidates when its copy expires and keeps its copy on a 304.

::: warning Not on a stream
Do not declare it on a handler returning `IAsyncEnumerable<T>`. Holding a stream back to hash it is
buffering it.
:::

## Write your own validator

A handler that knows its resource's version writes it, and is passed straight through rather than
held back:

```csharp
[Get("/documents/{id}")]
[ConditionalGet]
public Document Read(string id, IExecutionContext context) {
    var document = _documents.Find(id);

    context.Response.Headers[KnownHeaders.ETag] = "\"" + document.Version + "\"";
    context.Response.Headers[KnownHeaders.LastModified] = HttpDate.Format(document.UpdatedAt);

    return document;
}
```

This costs the body, not the handler. A handler that wrote its own validator ran in order to write
it. Skipping the work would need the validator before the handler runs, which is not built.

`If-None-Match` is evaluated when it is present, and `If-Modified-Since` only when it is not,
including when the tag does not match. That is RFC 9110 §13.2.1's order, implemented once and shared
with static content.

## Pair it with the response cache

```csharp
[Get("/rates/{symbol}")]
[CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["rates"])]
[ConditionalGet]
public Rate Read(string symbol) => _rates.Latest(symbol);
```

Every entry the response cache stores carries an entity-tag, computed as the response is captured
when the handler wrote none. The tag goes out with the miss and is replayed with the hit, so a
cached handler is never held back for a hash, and a revalidating caller is answered 304 without
running the handler and without sending the stored body.

The conditional stage sits outside the cache. That ordering is the design rather than a preference:
a validator filter placed inside the cache would never run on a hit, so a cached response could
never be revalidated.

A handler's own `ETag` is kept when the response is also cached. A version that changes when the
resource does says more than a hash of the serializer's output.

## What a 304 carries

The status, and the headers RFC 9110 §15.4.5 says a 304 repeats when a 200 would have sent them:
`ETag`, `Last-Modified`, `Cache-Control`, `Vary` and whatever else the handler and the filters wrote.
`Content-Type`, `Content-Length` and `Content-Encoding` are removed, because they describe content
the response does not have. A HEAD answered 304 reports no length for the same reason.

A 304 stands in for a 200 and nothing else. A 404 or a 500 is sent as it is, and so is a refusal.
Authorization and rate limiting record theirs ahead of this stage and the filter reads what they
recorded, so a caller who may not read the resource is not told that it has not changed.

## Compression and the tag

The tag covers the bytes as sent. The compression filter sits inside the conditional stage, so a
client that accepts gzip holds a weak tag, `W/"..."`, and an identity client holds the strong one.
Each is revalidated against its own, and `If-None-Match` compares weakly either way. This is the
same thing that happens for a compressed static file.

## Not built

- **`If-Match` and `If-Unmodified-Since`.** They guard a write against a lost update and answer 412,
  which needs the current validator before the handler runs.
- **Skipping the handler on a validator it knows.** Only a response-cache hit skips the handler
  today.
