using Amazon.Lambda.SQSEvents;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime.Impl;

public class SqsBatchFilter : IExecutionFilter {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly ISqsMessageContext _sqsMessageContext;
    
    public SqsBatchFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool, 
        ISqsMessageContext sqsMessageContext) {
        _jsonSerializer = jsonSerializer;
        _memoryStreamPool = memoryStreamPool;
        _sqsMessageContext = sqsMessageContext;
    }

    public async Task Execute(IExecutionChain chain) {
        var sqsEvent = await _jsonSerializer.DeserializeAsync<SQSEvent>(chain.Context.Request.Body);

        var batchItemFailure = new List<SQSBatchResponse.BatchItemFailure>();
        
        _sqsMessageContext.SqsEvent = sqsEvent;
        
        foreach (var sqsMessage in sqsEvent.Records) {
            _sqsMessageContext.Message = sqsMessage;
            
            if (!await ProcessMessage(chain, sqsMessage)) {
                batchItemFailure.Add(new SQSBatchResponse.BatchItemFailure{ItemIdentifier = sqsMessage.MessageId});
            }
        }

        await _jsonSerializer.SerializeAsync(chain.Context.Response.Body, new SQSBatchResponse(batchItemFailure));
    }

    private async Task<bool> ProcessMessage(IExecutionChain chain, SQSEvent.SQSMessage sqsMessage) {
        var context = (IExecutionContext)chain.Context.Clone();
        chain.Context.RequestServices.CreateScope();
        
        await using var bodyStream = new MemoryStreamPoolWrapper(_memoryStreamPool.Get());
        await using (var streamWriter = new StreamWriter(bodyStream)) {

            await streamWriter.WriteAsync(sqsMessage.Body);
        }

        bodyStream.Position = 0;
        context.Request.Body = bodyStream;

        var forkedChain = chain.Fork(context);

        try {
            await forkedChain.Next();
            
            return context.Response.Status is < 300;
        }
        catch (Exception exp) {
            
        }

        return false;
    }
}