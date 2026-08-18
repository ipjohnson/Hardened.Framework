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

    /// <param name="framing">
    /// What goes around each item. Defaults to newline-delimited JSON, which is what every
    /// streamed handler answered as before there was a choice.
    /// </param>
    public AsyncEnumerableIoFilter(
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        Func<IExecutionContext, Task> serializeResponse,
        Action<IExecutionContext>? headerActions,
        IStreamFraming? framing = null) {
        _deserializeRequest = deserializeRequest;
        _serializeResponse = serializeResponse;
        _headerActions = headerActions;
        _framing = framing ?? NdjsonFraming.Instance;
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
                context.Response.ContentType = _framing.ContentType;
                context.Response.ShouldSerialize = false;

                // Off for the whole stream, not per item. The buffered serializers open a
                // GZipStream per SerializeResponse call, so leaving this on would put a separate
                // gzip member on the wire for every item - legal concatenated gzip that no
                // streaming reader unpacks incrementally, which is the opposite of what a caller
                // reading a stream wants. Compressing a stream properly means one compressor
                // around the whole body, which is a different change.
                context.Response.ShouldCompress = false;

                await foreach (var item in asyncEnumerable.WithCancellation(context.CancellationToken)) {
                    context.Response.ResponseValue = item;

                    await _framing.WriteItem(context, _serializeResponse);

                    // Per item, which is the whole point of streaming: a caller reads the first
                    // result while the handler is still producing the rest.
                    await context.Response.Body.FlushAsync(context.CancellationToken);
                }

                await _framing.WriteCompletion(context);

                await context.Response.Body.FlushAsync(context.CancellationToken);
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
}
