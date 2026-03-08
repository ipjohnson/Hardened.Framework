using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

[SingletonService]
public class StreamingContextSerializationService : IContextSerializationService {
    private readonly ISerializationLocatorService _locatorService;
    private readonly IExceptionResponseSerializer _exceptionSerializer;
    private readonly INullValueResponseHandler _nullValueResponse;
    private readonly JsonSerializerOptions _serializerOptions;

    public StreamingContextSerializationService(
        ISerializationLocatorService locatorService,
        IExceptionResponseSerializer exceptionSerializer,
        INullValueResponseHandler nullValueResponse,
        IOptions<IJsonSerializerConfiguration> serializerConfiguration) {
        _locatorService = locatorService;
        _exceptionSerializer = exceptionSerializer;
        _nullValueResponse = nullValueResponse;
        _serializerOptions = serializerConfiguration.Value.SerializeOptions
            ?? new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
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
    private async Task SerializeAsyncEnumerable(
        IExecutionContext context, IAsyncEnumerable<object> enumerable) {
        context.Response.ContentType ??= "application/x-ndjson";
        context.Response.ShouldSerialize = false;

        await foreach (var item in enumerable.WithCancellation(context.CancellationToken)) {
            JsonSerializer.Serialize(context.Response.Body, item, item.GetType(), _serializerOptions);
            context.Response.Body.WriteByte((byte)'\n');
            await context.Response.Body.FlushAsync(context.CancellationToken);
        }
    }
}
