using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
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
        IBatchProcessorExceptionHandler bulkProcessorExceptionHandler,
        CurrentDdbRecordContext currentDdbRecordContext, 
        ILogger<DynamoDbExecutionFilter> logger) : base(
        jsonSerializer, 
        memoryStreamPool,
        bulkProcessorExceptionHandler, 
        logger) {
        _currentDdbRecordContext = currentDdbRecordContext;
    }

    protected override Task WriteRequestRecord(
        IExecutionChain chain, DynamoDBEvent.DynamodbStreamRecord record, Stream inputStream) {
        _currentDdbRecordContext.CurrentRecord = record;
        
        return Task.CompletedTask;
    }

    protected override async Task WriteResultsAsync(IExecutionContext context, IReadOnlyList<Result<DynamoDBEvent.DynamodbStreamRecord>> records) {
        var batchItemFailures = new List<StreamsEventResponse.BatchItemFailure>();

        foreach (var record in records) {
            if (!record.Success) {
                batchItemFailures.Add(new StreamsEventResponse.BatchItemFailure {ItemIdentifier = record.Value.EventID});
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