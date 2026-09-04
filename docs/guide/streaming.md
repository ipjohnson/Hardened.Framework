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

`[ServerSentEvents]` on a handler that does not return `IAsyncEnumerable<T>` is build error
`HRDW004`. There is no stream to frame, and ignoring it would leave an author believing a buffered
response was an event stream.

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

## Reconnection

A browser `EventSource` reconnects on its own after the stream ends or the connection drops, sending
the last id it saw as `Last-Event-ID`. Bind the header, replay from the event after it, and the
client resumes where it left off:

```csharp
[Get("/orders/live")]
[ServerSentEvents]
public async IAsyncEnumerable<SseItem<OrderEvent>> Live(
    [FromHeader(KnownHeaders.LastEventId)] string? lastEventId,
    IExecutionContext context) {
    await foreach (var order in _orders.Since(lastEventId)) {
        yield return new SseItem<OrderEvent>(order, Id: order.Sequence);
    }
}
```

The client stops reconnecting on a 204, and on any status other than 200. So a subscription that is
over for good ends by taking `IExecutionContext`, setting the status and yielding nothing:

```csharp
if (_orders.Closed(subscription)) {
    context.Response.Status = 204;

    yield break;
}
```

The pipeline writes no framing and no body for that 204.

::: warning Decide the 204 before the first item
A 204 after an event or a heartbeat has gone out is a 204 with a body, which the host refuses. The
client reads that as a network error and reconnects, which is the opposite of what the 204 was for.
:::

## Quiet streams are kept open

A stream that is silent for longer than the heartbeat interval carries a `: keep-alive` comment,
which the client discards. Fifteen seconds by default, which is what the WHATWG standard suggests
and half of the tightest idle cut on the list: CloudFront drops a response that is quiet for 30
seconds between packets, and retries the request while the first invocation keeps streaming to
nobody.

```csharp
services.ConfigureStreaming(streaming => {
    streaming.HeartbeatInterval = TimeSpan.FromSeconds(5);
});
```

Zero turns it off. Only a framing with something to write honours it, so newline-delimited JSON
stays silent whatever this says.

Every event stream also carries `Cache-Control: no-cache` and `X-Accel-Buffering: no` unless the
handler set them itself. The second is what stops nginx and several managed proxies buffering the
response into nothing.

## Refusals and failures

The content type is committed at the first byte rather than before the sequence is enumerated, which
is what makes the two cases differ.

A refusal on the route, an authorization failure, a binding failure or a throw before the first
event, leaves as its own status with a JSON body. The client stops.

A failure after the first event ends the stream, because the bytes are already with the client.
The client comes back with `Last-Event-ID`.

`[Retry]` retries the call that produces the sequence and never the enumeration, so an event is never
duplicated by a retry. A retry stops once the response has started.

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
| Lambda, `HARDENED_LAMBDA_RESPONSE_MODE=stream` | yes |
| Lambda, `HARDENED_LAMBDA_RESPONSE_MODE=buffered` | **no** |

On Lambda, whether a response streams is a deployment setting rather than a property of the
handler, because the front doors are strict about the wire protocol and the function cannot tell
them apart from the event. `stream` needs a function URL in `RESPONSE_STREAM` invoke mode, with or
without CloudFront in front. A buffered deployment accumulates the whole body before returning it,
so a streamed response arrives all at once at the end. An application with `[ServerSentEvents]`
handlers deployed in buffered mode logs a warning at startup naming them.

See [API Gateway](/aws/lambda-web#response-mode) for how the mode and the invoke mode are set
together.

## Compression

An event stream is not compressed by default. `text/event-stream` is on the compression
configuration's excluded list, by convention rather than necessity: an `EventSource` reconnects on
its own and a proxy in front of the service is the more usual place to compress one.

Newline-delimited JSON is compressed when the application enables
[compression](/guide/compression), as one member flushed per item, so a reader still gets each line
as it is produced.
