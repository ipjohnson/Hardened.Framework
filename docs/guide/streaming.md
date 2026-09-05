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

```
GET /measurements/live
HTTP/1.1 200 OK
Content-Type: application/x-ndjson

{"sensor":"north","reading":12}
{"sensor":"south","reading":41}
```

Nothing else changes. Binding, validation, filters and serializers work as they do on any route.

## Pick a framing

| | Media type | Declare it with |
|---|---|---|
| **NDJSON** | `application/x-ndjson` | nothing. This is the default |
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

`[ServerSentEvents]` on a handler that does not return `IAsyncEnumerable<T>` is build error
`HRDW004`.

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

`Id` is the one to reach for. A browser `EventSource` reconnects on its own and sends the last id
it saw as `Last-Event-ID`, so a stream that sets ids can resume. `Retry` sets how long the client
waits before reconnecting, in milliseconds; send it once, not on every event.

Only the payload is serialized into `data:`. `SseItem<Measurement>` puts the `Measurement` on the
wire, which is also what the OpenAPI document says.

## Reconnection

A browser `EventSource` reconnects on its own after the stream ends or the connection drops,
sending the last id it saw as `Last-Event-ID`. Bind the header, replay from the event after it,
and the client resumes where it left off:

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

The client stops reconnecting on a 204, and on any status other than 200. A subscription that is
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
client reads that as a network error and reconnects, which is the opposite of what the 204 was
for.
:::

## Quiet streams are kept open

A stream that is silent for longer than the heartbeat interval carries a `: keep-alive` comment,
which the client discards. The default is fifteen seconds, half of CloudFront's thirty-second
idle cut:

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

The content type is committed at the first byte rather than before the sequence is enumerated,
which is what makes the two cases differ.

A refusal on the route, an authorization failure, a binding failure or a throw before the first
event leaves as its own status with a JSON body. The client stops.

A failure after the first event ends the stream, because the bytes are already with the client.
The client comes back with `Last-Event-ID`.

`[Retry]` retries the call that produces the sequence and never the enumeration, so an event is
never duplicated by a retry. A retry stops once the response has started.

## Cancellation

The pipeline enumerates under the request's `CancellationToken`, so the `await foreach` inside
your handler stops when the client disconnects. Take the token as a parameter to pass it on:

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

Streaming is described with OpenAPI 3.2's `itemSchema`, and the complete content as an array of
the item under `schema`:

```yaml
responses:
  '200':
    content:
      text/event-stream:
        schema:
          type: array
          items:
            $ref: '#/components/schemas/Measurement'
        itemSchema:
          $ref: '#/components/schemas/Measurement'
```

The two answer different questions, and 3.2 allows both. `itemSchema` says each item is a
`Measurement` and that they arrive one after another. `schema` says what the complete content is,
which the specification defines for a sequential media type as the items treated as an array, and
it is what a reader without 3.2 sees: Refitter takes a stream's element type from it, and Kiota
reads neither for these media types and hands back a raw `Stream`. The item alone under `schema`
is never written, because it would say the response is one item.

This works in both directions for an OpenAPI contract. A document declaring `itemSchema`
generates a service interface returning `IAsyncEnumerable<T>` and a handler that streams it with
the framing the media type names, so an OpenAPI-first application streams by writing the
document. A Smithy model streams by binding a `@streaming` union as the output's `@httpPayload`,
which is Smithy's own event stream: the interface returns `IAsyncEnumerable<TUnion>`, each item
goes out as one member under `data:` with the member's name as `event:`, and the document
describes the item as the choice of its members.

`itemSchema` needs a 3.2 document, which is the default. Pin to `3.0.0` or `3.1.0` and you get
build warning `HRDOA002` naming the handler; the operation keeps the array under `schema`, so a
client generated from it reads a list rather than a stream.

## Where it works

| Host | Streams |
|---|---|
| Kestrel | yes |
| ASP.NET Core | yes |
| Lambda, `HARDENED_LAMBDA_RESPONSE_MODE=stream` | yes |
| Lambda, `HARDENED_LAMBDA_RESPONSE_MODE=buffered` | no |

On Lambda, whether a response streams is a deployment setting rather than a property of the
handler. `stream` needs a function URL in `RESPONSE_STREAM` invoke mode, with or without
CloudFront in front. A buffered deployment accumulates the whole body before returning it, so a
streamed response arrives all at once at the end. An application with `[ServerSentEvents]`
handlers deployed in buffered mode logs a warning at startup naming them. See
[Response mode](/aws/lambda-web#response-mode).

## Compression

An event stream is not compressed by default. `text/event-stream` is on the compression
configuration's excluded list, because an `EventSource` reconnects on its own and a proxy in
front of the service is the more usual place to compress one. Newline-delimited JSON is
compressed when the application enables [compression](/guide/compression), as one member flushed
per item, so a reader still gets each line as it is produced.

## Next

- [Sending requests](/guide/testing-web#the-response): reading a streamed body in a test
- [Compression](/guide/compression): what is and is not compressed
- [API Gateway](/aws/lambda-web#response-mode): streaming from Lambda
