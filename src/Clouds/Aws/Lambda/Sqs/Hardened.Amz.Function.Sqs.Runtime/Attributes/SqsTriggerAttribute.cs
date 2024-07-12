using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Amz.Function.Sqs.Runtime.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SqsTriggerAttribute : Attribute, IRequestFilterProvider {
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
        throw new NotImplementedException();
    }
}