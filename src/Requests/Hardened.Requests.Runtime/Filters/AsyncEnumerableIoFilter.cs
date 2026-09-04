using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class AsyncEnumerableIoFilter<TItem> : IExecutionFilter {
    private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
    private readonly Func<IExecutionContext, Task> _serializeResponse;
    private readonly Action<IExecutionContext>? _headerActions;
    private readonly IStreamFraming _framing;
    private readonly TimeSpan _heartbeatInterval;

    /// <param name="framing">
    /// What goes around each item. Defaults to newline-delimited JSON, which is what every
    /// streamed handler answered as before there was a choice.
    /// </param>
    /// <param name="heartbeatInterval">
    /// How long the handler may be quiet before the framing is asked to write a heartbeat, or zero
    /// for never - which is what a filter built without one does, and what
    /// <c>IOFilterProvider</c> replaces with the configured interval.
    /// </param>
    public AsyncEnumerableIoFilter(
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        Func<IExecutionContext, Task> serializeResponse,
        Action<IExecutionContext>? headerActions,
        IStreamFraming? framing = null,
        TimeSpan heartbeatInterval = default) {
        _deserializeRequest = deserializeRequest;
        _serializeResponse = serializeResponse;
        _headerActions = headerActions;
        _framing = framing ?? NdjsonFraming.Instance;
        _heartbeatInterval = heartbeatInterval;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // A request that is already decided does not have its body read, for the same reason it is
        // not read in IoFilter: a requirement settled over grants alone refuses before serialization
        // precisely so that a request presenting no credential does not cost a 10 MB deserialization
        // before it is rejected. Binding here would hand that position straight back.
        //
        // A streamed route is where it matters most. The body is read whole either way - streaming
        // describes the response, not the request - so the refused request pays the same price here
        // as anywhere else, and the routes carrying large uploads are disproportionately the ones
        // that stream their answers back.
        //
        // No bind duration is recorded either, because no bind was attempted - a zero would read as
        // a very fast deserialization rather than none.
        if (context.Response.ExceptionValue == null) {
            var bindParameterStartTimestamp = MachineTimestamp.Now;

            try {
                if (context.Request.Parameters == null) {
                    context.Request.Parameters = await _deserializeRequest(chain.Context);
                }
            }
            catch (Exception exp) {
                chain.Context.RequestServices.GetRequiredService<IRequestLogger>()
                    .RequestParameterBindFailed(chain.Context, exp);

                chain.Context.Response.ExceptionValue = exp;
            }
            finally {
                context.RequestMetrics.Record(RequestMetrics.ParameterBindDuration,
                    bindParameterStartTimestamp.GetElapsedMilliseconds());
            }
        }

        if (chain.Context.Response.ExceptionValue == null) {
            try {
                await chain.Next();
            }
            catch (Exception exp) {
                chain.Context.Response.ExceptionValue = exp;
            }
        }

        var responseTimestamp = MachineTimestamp.Now;

        try {
            _headerActions?.Invoke(chain.Context);

            if (chain.Context.Response.ExceptionValue != null) {
                await _serializeResponse(chain.Context);

                chain.Context.Response.ShouldSerialize = false;
            }
            else if (chain.Context.Response.ResponseValue is IAsyncEnumerable<TItem> asyncEnumerable) {
                context.Response.ShouldSerialize = false;

                // A stream that failed before it began is answered the way a refusal is: as an
                // error document under its own status. The failure is on the response by the time
                // this returns false, and nothing has reached the wire.
                if (!await WriteStream(context, asyncEnumerable)) {
                    await _serializeResponse(chain.Context);
                }
            }
            else if (chain.Context.Response.ShouldSerialize) {
                await _serializeResponse(chain.Context);

                chain.Context.Response.ShouldSerialize = false;
            }
        }
        finally {
            context.RequestMetrics.Record(RequestMetrics.ResponseDuration, responseTimestamp.GetElapsedMilliseconds());
        }
    }

    /// <summary>
    /// Every item, each flushed as it is written, with a heartbeat wherever the handler is quiet
    /// for longer than the interval.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The stream commits to its content type at the first byte, not before.</b> A handler that
    /// has nothing more to say answers a reconnect with a 204, and it says so from inside its
    /// iterator - which runs at the first <c>MoveNextAsync</c>, after the handler call returned.
    /// Committing before enumerating would put <c>text/event-stream</c> and a completion comment on
    /// that 204, and Kestrel refuses a body on a 204 by aborting the connection, which the client
    /// reads as a network error and reconnects from: the opposite of what the status is for. So the
    /// content type, the event-stream headers and the completion bytes all wait for the first item,
    /// the first heartbeat, or the end of a stream that is not a 204.
    /// </para>
    /// <para>
    /// An explicit enumerator rather than <c>await foreach</c>, so the token still reaches a
    /// handler's <c>[EnumeratorCancellation]</c> parameter and so the wait for the next item is a
    /// task that can be raced against the heartbeat timer. The race is only run when the handler
    /// has not already answered: a stream at a thousand items a second never starts a timer, and
    /// one that does start is cancelled the moment the item arrives rather than left to expire.
    /// </para>
    /// <para>
    /// The handler's enumerator does not touch the response body - a handler that writes to the
    /// body itself is not a streaming handler - so a heartbeat can never land inside an item.
    /// </para>
    /// </remarks>
    /// <returns>
    /// False when the handler failed before anything was committed, with the failure recorded on
    /// the response and nothing written; true otherwise. A failure after the first byte propagates
    /// - the bytes are with the client, so the only honest answer is to end the stream, and the
    /// client comes back with <c>Last-Event-ID</c>.
    /// </returns>
    private async Task<bool> WriteStream(IExecutionContext context, IAsyncEnumerable<TItem> stream) {
        var cancellationToken = context.CancellationToken;
        var response = context.Response;

        // Off once the framing says it has nothing to write, and off from the start at zero.
        var progress = new Progress { Heartbeats = _heartbeatInterval > TimeSpan.Zero };

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        while (true) {
            bool hasItem;

            try {
                var moveNext = enumerator.MoveNextAsync();

                hasItem = progress.Heartbeats && !moveNext.IsCompleted
                    ? await MoveNextWithHeartbeats(context, moveNext.AsTask(), progress)
                    : await moveNext;
            }
            catch (Exception exception) when (!progress.Committed && !cancellationToken.IsCancellationRequested) {
                response.ExceptionValue = exception;

                return false;
            }

            if (!hasItem) {
                break;
            }

            Commit(response, progress);

            response.ResponseValue = enumerator.Current;

            await _framing.WriteItem(context, _serializeResponse);

            // Per item, which is the whole point of streaming: a caller reads the first result
            // while the handler is still producing the rest. Through a compressing body this is a
            // sync flush on the encoder, so a compressed stream is one member delivered item by
            // item rather than one member per item.
            await response.Body.FlushAsync(cancellationToken);
        }

        // Nothing was written and the handler said there is nothing to write. No content type, no
        // completion bytes: a 204 with a body is not a 204.
        if (!progress.Committed && response.Status is 204 or 304) {
            return true;
        }

        Commit(response, progress);

        await _framing.WriteCompletion(context);

        await response.Body.FlushAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// What one stream has done so far. Shared between the loop and the heartbeat race, and read
    /// by the exception filter, which is why it is an object rather than a pair of locals: a
    /// failure after a heartbeat has to be seen as a failure after the first byte.
    /// </summary>
    private sealed class Progress {
        public bool Committed;

        public bool Heartbeats;
    }

    /// <summary>
    /// Waits for the next item, writing a heartbeat each time the interval passes first.
    /// </summary>
    /// <remarks>
    /// Turns heartbeats off for the rest of the stream once the framing answers that it has nothing
    /// to write. The stream is committed before the framing is asked, because a heartbeat is bytes
    /// and the headers have to be ahead of them - so a framing that declines has committed the
    /// content type a little earlier than its first item would have, which changes nothing on the
    /// wire.
    /// </remarks>
    private async Task<bool> MoveNextWithHeartbeats(
        IExecutionContext context, Task<bool> moveNext, Progress progress) {
        var cancellationToken = context.CancellationToken;
        var response = context.Response;

        while (!moveNext.IsCompleted && !cancellationToken.IsCancellationRequested) {
            // Linked so a client that goes away releases the timer with everything else, and
            // cancelled when the item wins so the timer is released rather than left to expire.
            using var pause = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var delay = Task.Delay(_heartbeatInterval, pause.Token);

            if (await Task.WhenAny(moveNext, delay) == moveNext) {
                pause.Cancel();

                break;
            }

            if (delay.IsCanceled) {
                break;
            }

            Commit(response, progress);

            if (!await _framing.WriteHeartbeat(context)) {
                progress.Heartbeats = false;

                break;
            }

            await response.Body.FlushAsync(cancellationToken);
        }

        return await moveNext;
    }

    /// <summary>
    /// The content type and, for an event stream, the two headers it needs from whatever sits
    /// between it and the client. Once, before the first byte.
    /// </summary>
    /// <remarks>
    /// <c>Cache-Control: no-cache</c> is what the standard's examples carry, and what keeps a
    /// shared cache from storing a stream and replaying it. <c>X-Accel-Buffering: no</c> is
    /// nginx's per-response switch for proxy buffering; everything else ignores it. Both defer to
    /// a handler or a filter that already set them. Only for an event stream: a newline-delimited
    /// response is an ordinary representation a cache may keep, so it is left to say for itself.
    /// </remarks>
    private void Commit(IExecutionResponse response, Progress progress) {
        if (progress.Committed) {
            return;
        }

        progress.Committed = true;

        response.ContentType = _framing.ContentType;

        if (string.Equals(_framing.ContentType, KnownContentType.EventStream, StringComparison.OrdinalIgnoreCase)) {
            var headers = response.Headers;

            if (!headers.ContainsKey(KnownHeaders.CacheControl)) {
                headers[KnownHeaders.CacheControl] = "no-cache";
            }

            if (!headers.ContainsKey(KnownHeaders.XAccelBuffering)) {
                headers[KnownHeaders.XAccelBuffering] = "no";
            }
        }
    }
}
