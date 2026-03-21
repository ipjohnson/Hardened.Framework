using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Testing;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Hardened.Amz.Function.Lambda.Testing;

public class LambdaTestApp : TestContext {
    private readonly ILambdaFunctionImplService _functionImplService;
    private readonly IOptions<IJsonSerializerConfiguration> _serializerOptions;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public LambdaTestApp(
        TestCancellationToken cancellationToken,
        ILambdaFunctionImplService functionImplService,
        IOptions<IJsonSerializerConfiguration> serializerOptions,
        IMemoryStreamPool memoryStreamPool,
        ILogger<LambdaTestApp> logger)
        : base(cancellationToken.Token, logger) {
        _functionImplService = functionImplService;
        _serializerOptions = serializerOptions;
        _memoryStreamPool = memoryStreamPool;
    }

    public async Task<T> Invoke<T>(string lambdaName, object payload, Action<TestLambdaContext>? contextAction = null) {
        await using var response = await Invoke(lambdaName, payload, contextAction);

        return (await JsonSerializer.DeserializeAsync<T>(
            response,
            _serializerOptions.Value.DeSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web),
            CancellationRequest))!;
    }

    public async Task<Stream> Invoke(string lambdaName, object payload,
        Action<TestLambdaContext>? contextAction = null) {
        using var memoryStreamReservation = _memoryStreamPool.Get();

        var context = TestLambdaContext.FromName(lambdaName);

        contextAction?.Invoke(context);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _serializerOptions.Value.SerializeOptions);

        memoryStreamReservation.Item.Write(payloadBytes, 0, payloadBytes.Length);
        memoryStreamReservation.Item.Position = 0;

        return await _functionImplService.InvokeFunction(memoryStreamReservation.Item, context);
    }

    /// <summary>
    /// Invoke a Lambda function with a raw JSON string, bypassing .NET serialization.
    /// This exercises the exact deserialization path that production uses, catching
    /// AOT source-generator gaps that object-based Invoke() would mask.
    /// </summary>
    public async Task<Stream> InvokeRaw(string lambdaName, string rawJson,
        Action<TestLambdaContext>? contextAction = null) {
        using var memoryStreamReservation = _memoryStreamPool.Get();

        var context = TestLambdaContext.FromName(lambdaName);

        contextAction?.Invoke(context);

        var payloadBytes = Encoding.UTF8.GetBytes(rawJson);
        memoryStreamReservation.Item.Write(payloadBytes, 0, payloadBytes.Length);
        memoryStreamReservation.Item.Position = 0;

        return await _functionImplService.InvokeFunction(memoryStreamReservation.Item, context);
    }

    /// <summary>
    /// Invoke a Lambda function with raw JSON and deserialize the response.
    /// </summary>
    public async Task<T> InvokeRaw<T>(string lambdaName, string rawJson,
        Action<TestLambdaContext>? contextAction = null) {
        await using var response = await InvokeRaw(lambdaName, rawJson, contextAction);

        return (await JsonSerializer.DeserializeAsync<T>(
            response,
            _serializerOptions.Value.DeSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web),
            CancellationRequest))!;
    }
}