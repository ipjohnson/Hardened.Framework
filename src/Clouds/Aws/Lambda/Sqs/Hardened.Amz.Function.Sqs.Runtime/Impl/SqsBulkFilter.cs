using System.Text;
using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Amz.Shared.Lambda.Runtime.Filter;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Logging;

namespace Hardened.Amz.Function.Sqs.Runtime.Impl;

[Expose(typeof(SqsBatchFilter))]
[Singleton]
public class SqsBatchFilter : BaseBatchExecutionFilter<SQSEvent, SQSEvent.SQSMessage> {

    public SqsBatchFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool,
        IBatchProcessorExceptionHandler bulkProcessorExceptionHandler,
        ILogger<SqsBatchFilter> logger) :
        base(
            jsonSerializer,
            memoryStreamPool,
            bulkProcessorExceptionHandler,
            logger) {
    }

    protected override Task<IExecutionChain> WriteRequestRecord(
        IExecutionChain chain,
        SQSEvent.SQSMessage record, Stream inputStream, Stream outputStream) {
        
        
        inputStream.Write(Encoding.UTF8.GetBytes(record.Body));
        inputStream.Position = 0;

        var request = 
            new LambdaExecutionRequest(chain.Context.Request.Method, chain.Context.Request.Path, inputStream, chain.Context.Request.Headers);
        var response = 
            new LambdaExecutionResponse(outputStream, new HeaderCollectionStringValues());
        
        var context = chain.Context.Clone(request,response);
        
        return Task.FromResult(chain.Fork(context));
    }

    protected  override async Task WriteResultsAsync(
        IExecutionContext context, IReadOnlyList<Result<SQSEvent.SQSMessage>> results) {
        var batchItemFailure = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var result in results) {
            if (!result.Success) {
                batchItemFailure.Add(new SQSBatchResponse.BatchItemFailure{ ItemIdentifier = result.Value.MessageId});
            }
        }
        
        await JsonSerializer.SerializeAsync(context.Response.Body, new SQSBatchResponse(batchItemFailure));
    }

    protected override IEnumerable<SQSEvent.SQSMessage> ReadRecords(SQSEvent tEvent) {
        return tEvent.Records;
    }
}