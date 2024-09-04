using Hardened.Amz.Function.Sqs.Runtime.Impl;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SqsTriggerAttribute : Attribute, IRequestFilterProvider {
    private SqsBatchFilter? _sqsBatchFilter;
    
    public SqsTriggerAttribute(string queueName) {
        QueueName = queueName;
    }
    
    public string QueueName { get; }

    /// <summary>
    /// Batch size
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Batch window in seconds
    /// </summary>
    public int BatchWindow { get; set; } = 10;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(
            context => _sqsBatchFilter ??= 
                context.RootServiceProvider.GetRequiredService<SqsBatchFilter>(), 
            -10);
    }
}