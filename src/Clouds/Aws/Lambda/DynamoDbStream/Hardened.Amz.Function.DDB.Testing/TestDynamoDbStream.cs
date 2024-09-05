using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Testing;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.DDB.Testing;

public class TestDynamoDbStream : TestContext {
    private readonly ILambdaFunctionImplService _functionImplService;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly IJsonSerializer _jsonSerializer;

    public TestDynamoDbStream(
        ILogger<TestDynamoDbStream> logger,
        ILambdaFunctionImplService functionImplService, 
        IMemoryStreamPool memoryStreamPool,
        IJsonSerializer jsonSerializer) : base(CancellationToken.None, logger) {
        _functionImplService = functionImplService;
        _memoryStreamPool = memoryStreamPool;
        _jsonSerializer = jsonSerializer;
    }
    
    public async Task<StreamsEventResponse> ProcessUpdates(params DynamoDBEvent.DynamodbStreamRecord[] records) {
        var stream = _memoryStreamPool.Get();
        await _jsonSerializer.SerializeAsync(stream.Item, new DynamoDBEvent {
            Records = records
        });

        stream.Item.Position = 0;

        var responseStream = await _functionImplService.InvokeFunction(stream.Item, TestLambdaContext.FromName("Process"));

        responseStream.Position = 0;
        
        return await _jsonSerializer.DeserializeAsync<StreamsEventResponse>(responseStream);
    }
}