using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hardened.Amz.Function.Lambda.Testing;

public class LambdaTestApp
{
    private readonly ILambdaFunctionImplService _functionImplService;
    private readonly IOptions<IJsonSerializerConfiguration> _serializerOptions;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public LambdaTestApp(
        ILambdaFunctionImplService functionImplService,
        IOptions<IJsonSerializerConfiguration> serializerOptions, 
        IMemoryStreamPool memoryStreamPool)
    {
        _functionImplService = functionImplService;
        _serializerOptions = serializerOptions;
        _memoryStreamPool = memoryStreamPool;
    }

    public async Task<T> Invoke<T>(string lambdaName, object payload, Action<TestLambdaContext>? contextAction = null)
    {
        await using var response = await Invoke(lambdaName, payload, contextAction);
            
        return (await JsonSerializer.DeserializeAsync<T>(
            response, _serializerOptions.Value.DeSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    public async Task<Stream> Invoke(string lambdaName, object payload, Action<TestLambdaContext>? contextAction = null)
    {
        using var memoryStreamReservation = _memoryStreamPool.Get();

        var context = TestLambdaContext.FromName(lambdaName);

        contextAction?.Invoke(context);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _serializerOptions.Value.SerializeOptions);

        memoryStreamReservation.Item.Write(payloadBytes, 0, payloadBytes.Length);
        memoryStreamReservation.Item.Position = 0;

        return await _functionImplService.InvokeFunction(memoryStreamReservation.Item, context);
    }
}