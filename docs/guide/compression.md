# Compression

A JSON catalogue is mostly repeated keys, and gzip takes it to a fifth of its size. Hardened
compresses a response when the application asks for it, and decodes a compressed request body for
every application whether it asks or not.

## Responses

Nothing is compressed until something declares it. Turn it on for the whole application:

```csharp
[HardenedModule]
[Enable<HardenedCompression>]
[KestrelRuntime]
public partial class Application { }
```

Or for one operation, or one class:

```csharp
[Get("/catalog")]
[Compress]
public Catalog Browse() => _catalog.Current;
```

gzip is offered first and Brotli behind it, both at the fastest level.

The coding is negotiated from the client's `Accept-Encoding` when the filter is entered. Whether the
body is compressed at all is decided on the first write, which is the first moment the status and
the content type are both known and nothing has yet reached the transport. So the headers are final
before the response starts, on every host.

An operation carrying `[Compress]` is left alone by the application-wide default, so a declaration
on the operation is the one that applies.

### Favour a coding

```csharp
[Get("/catalog")]
[Compress(Favor = CompressionType.Br)]
public Catalog Browse() => _catalog.Current;
```

`Favor` moves one of the configured codings to the front for this operation. It reorders, it does
not enable: a coding the configuration does not offer cannot be favoured into existence.

### Decide per response

The media-type rule is a reasonable default and it cannot know that a particular list is usually
three items long. A predicate sees the value the handler returned:

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

The arguments reach the predicate through `Create`, the same shape a cache key provider uses. They
are `object[]` rather than `string[]`, so a count written as `50` arrives as an integer. `Create`
runs once per handler as the filter chain is built, so a predicate handed arguments it cannot use
fails there, naming the handler, rather than on a request.

The predicate replaces the media-type rule for that operation, so it can also opt in a type the
default list leaves out. It is not consulted for a response replayed from the response cache, which
carries no handler value; that follows the default rule.

::: warning One declaration per operation
`[Compress]` on a method and on its class at the same time is build error `HRDW003`. At run time the
inner filter finds the body already wrapped and stands down, so a slip cannot produce two encoders.
:::

### What is not compressed

A response that already carries a `Content-Encoding`, and a 204, 206 or 304, which either have no
body or carry a byte range. An offset into a compressed stream is not an offset into the resource.

Everything else is decided by content type. The default list is JSON, problem JSON and any
`+json`; XML and any `+xml`; JavaScript; NDJSON; SVG; and `text/*`, less `text/event-stream`.

Event streams are excluded by convention rather than necessity. A sync flush per event does keep a
stream live through an encoder, but an `EventSource` reconnects on its own and a proxy in front of
the service is the more usual place to compress one. Newline-delimited JSON is compressed, as one
member, flushed per item.

## Requests

A compressed request body is decoded before anything reads it, in every application, with no
declaration. The filter sits ahead of the bind, so forms, Newtonsoft payloads and raw bodies can all
arrive compressed. It sits ahead of the response cache too, so `ByPayload` hashes identity bytes and
a gzip body shares an entry with its plain twin.

**The decoded size is capped**, at 30 MB by default. A few hundred bytes of gzip can decode to
gigabytes, and the host's own request body limit is measured on the wire. Reading past the cap
answers 413.

A coding the filter does not know answers 415 carrying `Accept-Encoding: gzip, br`.

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
| `Level` | `CompressionLevel.Fastest`, as ASP.NET Core chose |
| `MediaTypes` | The list above. A pattern is an exact media type, `type/*`, or `type/*+suffix` |
| `ExcludedMediaTypes` | `text/event-stream` |
| `MaxDecompressedRequestBytes` | `30_000_000` |

This is an amender rather than a replacement, so it composes with the defaults. It applies whether
or not `[Enable<HardenedCompression>]` is written, because the request-side cap applies to every
application.

## How it composes

The compression filter sits just outside the response cache. The cache therefore stores identity
bytes and the filter encodes a hit on the way out, so one entry serves a gzip client and a plain one.
`Content-Encoding` is never stored with an entry.

It sits inside the conditional stage, so a 304 carries no coding for a body it does not have. The
entity-tag a conditional response is judged on covers the bytes as sent, which means a gzip client
and an identity client hold different tags for one resource and each is revalidated against its own.

`Vary` is merged rather than assigned, by this filter, by `VaryByHeader` and by CORS, so a response
that varies on three things says so once.

::: tip Do not add ASP.NET Core's middleware beside this
If it is present it sees the `Content-Encoding` this writes and stands down, so nothing breaks. It
is work for nothing.
:::
