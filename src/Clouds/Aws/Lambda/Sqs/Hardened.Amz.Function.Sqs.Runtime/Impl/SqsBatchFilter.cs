using System.Text;
using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Headers;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime.Impl;

/**
[Expose(typeof(SqsBatchFilter))]
[Singleton]
public class SqsBatchFilter : IExecutionFilter {
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly ISqsMessageContext _sqsMessageContext;
    private readonly ISqsExceptionHandler _sqsExceptionHandler;
    
    public SqsBatchFilter(
        IJsonSerializer jsonSerializer,
        IMemoryStreamPool memoryStreamPool, 
        ISqsMessageContext sqsMessageContext, 
        ISqsExceptionHandler sqsExceptionHandler) {
        _jsonSerializer = jsonSerializer;
        _memoryStreamPool = memoryStreamPool;
        _sqsMessageContext = sqsMessageContext;
        _sqsExceptionHandler = sqsExceptionHandler;
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
        using var outputStream = _memoryStreamPool.Get();
        
        chain.Context.RequestServices.CreateScope();

        using var inputStream = _memoryStreamPool.Get();
        
        inputStream.Item.Write(Encoding.UTF8.GetBytes(sqsMessage.Body));
        
        inputStream.Item.Position = 0;

        var request = 
            new LambdaExecutionRequest(chain.Context.Request.Method, chain.Context.Request.Path, inputStream.Item, chain.Context.Request.Headers);
        var response = 
            new LambdaExecutionResponse(outputStream.Item, new HeaderCollectionStringValues());
        var context = (IExecutionContext)chain.Context.Clone(request,response);
        var forkedChain = chain.Fork(context);
        
        try {
            await forkedChain.Next();
            
            return response.Status is null or < 300;
        }
        catch (Exception exp) {
            return await _sqsExceptionHandler.HandleException(forkedChain, sqsMessage, exp);
        }
    }
}
*/