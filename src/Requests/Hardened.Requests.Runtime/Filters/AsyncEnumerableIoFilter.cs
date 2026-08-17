using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class AsyncEnumerableIoFilter<TItem> : IExecutionFilter {
    private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
    private readonly Func<IExecutionContext, Task> _serializeResponse;
    private readonly Action<IExecutionContext>? _headerActions;

    public AsyncEnumerableIoFilter(
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        Func<IExecutionContext, Task> serializeResponse,
        Action<IExecutionContext>? headerActions) {
        _deserializeRequest = deserializeRequest;
        _serializeResponse = serializeResponse;
        _headerActions = headerActions;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
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
                context.Response.ContentType = KnownContentType.NdJson;
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
                    await _serializeResponse(context);
                    context.Response.Body.WriteByte((byte)'\n');
                    await context.Response.Body.FlushAsync(context.CancellationToken);
                }

                // Write a trailing newline so the streaming response body is never
                // empty. Lambda Function URLs don't close the body stream promptly
                // for zero-byte responses, causing downstream readers to hang.
                context.Response.Body.WriteByte((byte)'\n');
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
