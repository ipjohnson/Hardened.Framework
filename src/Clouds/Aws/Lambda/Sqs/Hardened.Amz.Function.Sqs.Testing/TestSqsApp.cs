using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Function.Sqs.Testing;


public class TestSqsApp : TestContext {
    private readonly ILambdaFunctionImplService _functionImplService;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public TestSqsApp(
        ILogger<TestSqsApp> logger,
        ILambdaFunctionImplService functionImplService, 
        IMemoryStreamPool memoryStreamPool,
        IJsonSerializer jsonSerializer) : base(CancellationToken.None, logger) {
        _functionImplService = functionImplService;
        _memoryStreamPool = memoryStreamPool;
        _jsonSerializer = jsonSerializer;
    }

    public async Task<SQSBatchResponse> SendMessage<T>(params T[] messages) {
        var sqsEvent = GenerateEvent(messages);

        using var stream = _memoryStreamPool.Get();
        await _jsonSerializer.SerializeAsync(stream.Item, sqsEvent);

        stream.Item.Position = 0;

        var responseStream = await _functionImplService.InvokeFunction(stream.Item, TestLambdaContext.FromName("Process"));

        responseStream.Position = 0;

        return await _jsonSerializer.DeserializeAsync<SQSBatchResponse>(responseStream);
    }

    private SQSEvent GenerateEvent<T>(T[] messages) {
        var list = new List<SQSEvent.SQSMessage>();

        foreach (var message in messages) {
            var serializedMessage = _jsonSerializer.Serialize(message!);
            
            list.Add(new SQSEvent.SQSMessage {
                Body = serializedMessage,
            });
        }
        
        return new SQSEvent {
            Records = list
        };
    }
}