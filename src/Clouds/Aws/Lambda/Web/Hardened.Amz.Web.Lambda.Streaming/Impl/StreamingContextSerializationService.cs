using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public class StreamingContextSerializationService : IContextSerializationService {
    private readonly ISerializationLocatorService _locatorService;
    private readonly IExceptionResponseSerializer _exceptionSerializer;
    private readonly INullValueResponseHandler _nullValueResponse;

    public StreamingContextSerializationService(
        ISerializationLocatorService locatorService,
        IExceptionResponseSerializer exceptionSerializer,
        INullValueResponseHandler nullValueResponse) {
        _locatorService = locatorService;
        _exceptionSerializer = exceptionSerializer;
        _nullValueResponse = nullValueResponse;
    }

    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        return _locatorService.FindRequestDeserializer(context).DeserializeRequestBody<T>(context);
    }

    public Task SerializeResponse(IExecutionContext context) {
        if (context.DefaultOutput != null) {
            return context.DefaultOutput(context);
        }

        if (context.Response.ExceptionValue != null) {
            return _exceptionSerializer.Handle(context, context.Response.ExceptionValue);
        }

        if (context.Response.ResponseValue == null) {
            return _nullValueResponse.Handle(context);
        }

        if (context.Response.ResponseValue is IAsyncEnumerable<object> asyncEnumerable) {
            return SerializeAsyncEnumerable(context, asyncEnumerable);
        }

        return _locatorService.FindResponseSerializer(context).SerializeResponse(context);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Streaming ndjson serializes concrete types known at compile time.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Streaming ndjson serializes concrete types known at compile time.")]
    private static async Task SerializeAsyncEnumerable(
        IExecutionContext context, IAsyncEnumerable<object> enumerable) {
        context.Response.ContentType ??= "application/x-ndjson";
        context.Response.ShouldSerialize = false;

        await foreach (var item in enumerable.WithCancellation(context.CancellationToken)) {
            JsonSerializer.Serialize(context.Response.Body, item, item.GetType());
            context.Response.Body.WriteByte((byte)'\n');
            await context.Response.Body.FlushAsync(context.CancellationToken);
        }
    }
}
