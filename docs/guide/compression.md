# Compression

A JSON catalogue is mostly repeated keys, and gzip takes it to a fifth of its size. Hardened
compresses a response when the application asks for it, and decodes a compressed request body for
every application whether it asks or not.

```csharp
[Get("/catalog")]
[Compress]
public Catalog Browse() => _catalog.Current;
```

```
GET /catalog
Accept-Encoding: gzip, br
HTTP/1.1 200 OK
Content-Encoding: gzip
Vary: Accept-Encoding
```

## Turning it on

For one operation or one class, `[Compress]` as above. For the whole application:

```csharp
[HardenedModule]
[Enable<HardenedCompression>]
[KestrelRuntime]
public partial class Application { }
```

An operation carrying `[Compress]` is left alone by the application-wide default, so the
declaration on the operation is the one that applies. Nothing is compressed until one of the two
is written.

gzip is offered first and Brotli behind it, both at the fastest level. The coding is negotiated
from the client's `Accept-Encoding` when the filter is entered. Whether the body is compressed at
all is decided on the first write, which is the first moment the status and the content type are
both known and nothing has yet reached the transport, so the headers are final before the
response starts, on every host.

## Favour a coding

```csharp
[Get("/catalog")]
[Compress(Favor = CompressionType.Br)]
public Catalog Browse() => _catalog.Current;
```

`Favor` moves one of the configured codings to the front for this operation. It reorders, it does
not enable: a coding the configuration does not offer cannot be favoured into existence.

## Decide per response

The media-type rule cannot know that a particular list is usually three items long. A predicate
sees the value the handler returned:

```csharp
[Get("/pets")]
[Compress<ListLargerThan>(50, Favor = CompressionType.Br)]
public Task<List<Pet>> List() => _pets.All();
```

```csharp
public sealed class ListLargerThan : ICompressionPredicate {
    private readonly int _count;

    private ListLargerThan(int count) => _count = count;

    public static ICompressionPredicate Create(object[] args) => args is [int count]
        ? new ListLargerThan(count)
        : throw new ArgumentException("ListLargerThan takes one integer.");

    public bool ShouldCompress(object value, IExecutionContext context) =>
        value is System.Collections.ICollection { Count: var n } && n > _count;
}
```

The arguments reach the predicate through `Create`, as `object[]`, so a count written as `50`
arrives as an integer. `Create` runs once per handler as the filter chain is built, so a predicate
handed arguments it cannot use fails there, naming the handler.

The predicate replaces the media-type rule for that operation, so it can also opt in a type the
default list leaves out. It is not consulted for a response replayed from the response cache,
which carries no handler value; that follows the default rule.

`[Compress]` on a method and on its class at the same time is build error `HRDW003`.

## What is not compressed

A response that already carries a `Content-Encoding`, and a 204, 206 or 304, which either have no
body or carry a byte range.

Everything else is decided by content type. The default list is JSON, problem JSON and any
`+json`; XML and any `+xml`; JavaScript; NDJSON; SVG; and `text/*`, less `text/event-stream`. An
event stream is excluded because an `EventSource` reconnects on its own and a proxy in front of
the service is the more usual place to compress one. Newline-delimited JSON is compressed, as one
member, flushed per item.

## Requests

A compressed request body is decoded before anything reads it, in every application, with no
declaration. The filter sits ahead of the bind, so forms, Newtonsoft payloads and raw bodies can
all arrive compressed. It sits ahead of the response cache too, so `ByPayload` hashes identity
bytes and a gzip body shares an entry with its plain twin.

The decoded size is capped at 30 MB by default. A few hundred bytes of gzip can decode to
gigabytes, and the host's own request body limit is measured on the wire. Reading past the cap
answers 413. A coding the filter does not know answers 415 carrying `Accept-Encoding: gzip, br`.

## Configuration

```csharp
services.ConfigureCompression(compression => {
    compression.Encodings = ["br", "gzip"];
    compression.MediaTypes.Add("application/wasm");
    compression.MaxDecompressedRequestBytes = 8_000_000;
});
```

| | Default |
|---|---|
| `Encodings` | `["gzip", "br"]`, in preference order. The first the client accepts is used |
| `Level` | `CompressionLevel.Fastest` |
| `MediaTypes` | The list above. A pattern is an exact media type, `type/*`, or `type/*+suffix` |
| `ExcludedMediaTypes` | `text/event-stream` |
| `MaxDecompressedRequestBytes` | `30_000_000` |

This is an amender rather than a replacement, so it composes with the defaults. It applies whether
or not `[Enable<HardenedCompression>]` is written, because the request-side cap applies to every
application.

## How it composes

The compression filter sits just outside the response cache. The cache stores identity bytes and
the filter encodes a hit on the way out, so one entry serves a gzip client and a plain one.
`Content-Encoding` is never stored with an entry.

It sits inside the conditional stage, so a 304 carries no coding for a body it does not have. The
entity-tag a conditional response is judged on covers the bytes as sent, so a gzip client and an
identity client hold different tags for one resource and each is revalidated against its own.

`Vary` is merged rather than assigned, by this filter, by `VaryByHeader` and by CORS, so a
response that varies on three things says so once.

::: tip Do not add ASP.NET Core's middleware beside this
If it is present it sees the `Content-Encoding` this writes and stands down, so nothing breaks. It
is work for nothing.
:::

## Next

- [Response caching](/guide/response-caching): the cache stores identity bytes
- [Conditional requests](/guide/conditional-requests): the weak tag a gzip client holds
- [Streaming responses](/guide/streaming#compression): what a stream does
