# Streaming responses

Return `IAsyncEnumerable<T>` and the response streams. The caller reads the first item while the
handler is still producing the rest.

```csharp
[BasePath("/measurements")]
public class MeasurementController {

    [Get("/live")]
    public async IAsyncEnumerable<Measurement> Live() {
        await foreach (var reading in _sensor.Read()) {
            yield return reading;
        }
    }
}
```

Nothing else changes. Binding, validation, filters and serializers work as they do on any route.

## Pick a framing

| | Media type | Declare it with |
|---|---|---|
| **NDJSON** | `application/x-ndjson` | nothing — this is the default |
| **Server-sent events** | `text/event-stream` | `[ServerSentEvents]` |

NDJSON writes one JSON document per line. Server-sent events is what a browser's `EventSource`
speaks, and what most model-inference APIs stream:

```csharp
[Get("/live")]
[ServerSentEvents]
public async IAsyncEnumerable<Measurement> Live() { … }
```

```
data: {"sensor":"north","reading":12}

data: {"sensor":"south","reading":41}

```

## Set the event fields

Yield `SseItem<T>` instead of a bare `T`:

```csharp
[Get("/live")]
[ServerSentEvents]
public async IAsyncEnumerable<SseItem<Measurement>> Live() {
    yield return new SseItem<Measurement>(reading, Id: reading.Sequence, Event: "reading");
}
```

```
id: 41
event: reading
data: {"sensor":"north","reading":12}

```

**`Id` is the one to reach for.** A browser `EventSource` reconnects on its own and sends the last
id it saw as `Last-Event-ID`, so a stream that sets ids can resume.

**`Retry`** sets how long the client waits before reconnecting, in milliseconds. Send it once, not
on every event.

Only the payload is serialized into `data:` — `SseItem<Measurement>` puts the `Measurement` on the
wire, which is also what the OpenAPI document says.

## Cancellation

The pipeline enumerates under the request's `CancellationToken`, so the `await foreach` inside your
handler stops when the client disconnects. Take the token as a parameter to pass it on:

```csharp
[Get("/live")]
public async IAsyncEnumerable<Measurement> Live(
    [EnumeratorCancellation] CancellationToken cancellationToken) {
    await foreach (var reading in _sensor.Read(cancellationToken)) {
        yield return reading;
    }
}
```

## What the document says

Streaming is described with OpenAPI 3.2's `itemSchema`:

```yaml
responses:
  '200':
    content:
      text/event-stream:
        itemSchema:
          $ref: '#/components/schemas/Measurement'
```

This works in both directions. A specification declaring `itemSchema` generates a service interface
returning `IAsyncEnumerable<T>`, so a spec-first application streams by writing the spec.

`itemSchema` needs a 3.2 document, which is the default. Pin to `3.0.0` or `3.1.0` and you get build
warning `HRDOA002` naming the handler; the operation is emitted with its media type and no schema.

## Where it works

| Host | Streams |
|---|---|
| Kestrel | yes |
| ASP.NET Core | yes |
| Lambda, streaming runtime | yes |
| Lambda, buffered API Gateway | **no** |

The buffered API Gateway runtime accumulates the whole body before returning it, so a streamed
response arrives all at once at the end. Deploy streaming endpoints behind the Lambda streaming
runtime or behind Kestrel.

## Compression

Streamed responses are never compressed. The response serializers compress per call, which on a
stream would put a separate gzip member on the wire for every item. For NDJSON, ask for compression
at the transport.
