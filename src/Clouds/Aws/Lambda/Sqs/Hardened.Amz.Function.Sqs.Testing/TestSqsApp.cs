using System.Globalization;
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

        // A batch where nothing failed writes no response body, which is how a real invocation
        // tells SQS that every message succeeded. Deserializing that threw
        // "The input does not contain any JSON tokens" - so this helper could only ever be used
        // for batches that failed, and the all-succeeded case, which is the common one, crashed.
        if (responseStream.Length == 0) {
            return new SQSBatchResponse(new List<SQSBatchResponse.BatchItemFailure>());
        }

        return await _jsonSerializer.DeserializeAsync<SQSBatchResponse>(responseStream);
    }

    /// <summary>
    /// Each message is identified by its position in the array handed to
    /// <see cref="SendMessage{T}"/>, so a caller correlates a failure back to the message that
    /// caused it.
    ///
    /// <para>
    /// The id matters more than it looks: a partial batch response names failed messages by
    /// <c>MessageId</c>, so leaving it unset produced a response whose every
    /// <c>ItemIdentifier</c> was null — the count of failures was right and which ones failed was
    /// unknowable, which is the one thing a batch handler test needs to assert.
    /// </para>
    /// </summary>
    private SQSEvent GenerateEvent<T>(T[] messages) {
        var list = new List<SQSEvent.SQSMessage>();

        for (var i = 0; i < messages.Length; i++) {
            list.Add(new SQSEvent.SQSMessage {
                MessageId = i.ToString(CultureInfo.InvariantCulture),
                Body = _jsonSerializer.Serialize(messages[i]!),
            });
        }

        return new SQSEvent {
            Records = list
        };
    }
}