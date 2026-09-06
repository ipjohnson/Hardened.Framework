using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.DDB.Runtime.Impl;

// Registered as the concrete type: FilterStartup resolves it by class to hand to the filter
// registry, and inference would otherwise pick an interface off BaseBatchExecutionFilter.
[TransientService(As = typeof(DynamoDbExecutionFilter))]
public class DynamoDbExecutionFilter : BaseBatchExecutionFilter<DynamoDBEvent, DynamoDBEvent.DynamodbStreamRecord> {
    private readonly CurrentDdbRecordContext _currentDdbRecordContext;
    
    public DynamoDbExecutionFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool, 
        IBatchProcessorExceptionHandler batchProcessorExceptionHandler,
        CurrentDdbRecordContext currentDdbRecordContext,
        IMetricLoggerProvider metricLoggerProvider,
        ILogger<DynamoDbExecutionFilter> logger) : base(
        jsonSerializer,
        memoryStreamPool,
        batchProcessorExceptionHandler,
        metricLoggerProvider,
        logger) {
        _currentDdbRecordContext = currentDdbRecordContext;
    }

    protected override Task WriteRequestRecord(
        IExecutionChain chain, DynamoDBEvent.DynamodbStreamRecord record, Stream inputStream) {
        _currentDdbRecordContext.CurrentRecord = record;
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports the failed records as batch item failures, identified by sequence number.
    ///
    /// <para>
    /// A DynamoDB stream is ordered per shard, and Lambda re-drives it by checkpoint rather than by
    /// message: it takes the lowest sequence number in this list and replays from there. The
    /// identifier therefore has to be one — <c>Dynamodb.SequenceNumber</c>. It was <c>EventID</c>
    /// until 2026-08-15, which is a record identifier and not a position in the stream, so Lambda
    /// could not resolve a checkpoint from it and fell back to re-driving the whole batch. That is
    /// the outcome partial batch reporting exists to avoid, and it looked like it was working.
    /// </para>
    ///
    /// <para>
    /// The SQS sibling has always been right, because a standard queue identifies a message rather
    /// than a position and <c>MessageId</c> is the correct answer there. The two filters look
    /// symmetrical and are not.
    /// </para>
    /// </summary>
    protected override async Task WriteResultsAsync(IExecutionContext context, IReadOnlyList<Result<DynamoDBEvent.DynamodbStreamRecord>> records) {
        var batchItemFailures = new List<StreamsEventResponse.BatchItemFailure>();

        foreach (var record in records) {
            if (!record.Success) {
                batchItemFailures.Add(new StreamsEventResponse.BatchItemFailure {
                    ItemIdentifier = record.Value.Dynamodb.SequenceNumber
                });
            }
        }

        var streamsEventResponse = new StreamsEventResponse();

        if (batchItemFailures.Count > 0) {
            streamsEventResponse.BatchItemFailures = batchItemFailures;
        }

        await JsonSerializer.SerializeAsync(context.Response.Body, streamsEventResponse);
    }

    protected override IEnumerable<DynamoDBEvent.DynamodbStreamRecord> ReadRecords(DynamoDBEvent tEvent) {
        return tEvent.Records;
    }
}