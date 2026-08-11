using Amazon.Lambda.SQSEvents;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Function.Sqs.Runtime;

public interface ISqsMessageContext {
    SQSEvent? SqsEvent { get; set; }
    
    SQSEvent.SQSMessage? Message { get; set; }
}

[SingletonService]
public class SqsMessageContext : ISqsMessageContext {
    public SQSEvent? SqsEvent { get; set; }
    
    public SQSEvent.SQSMessage? Message { get; set; }
}