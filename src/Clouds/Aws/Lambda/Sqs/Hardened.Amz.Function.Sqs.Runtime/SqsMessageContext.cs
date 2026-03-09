using Amazon.Lambda.SQSEvents;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Function.Sqs.Runtime;

public interface ISqsMessageContext {
    SQSEvent? SqsEvent { get; set; }
    
    SQSEvent.SQSMessage? Message { get; set; }
}

[Expose]
[Singleton]
public class SqsMessageContext : ISqsMessageContext {
    public SQSEvent? SqsEvent { get; set; }
    
    public SQSEvent.SQSMessage? Message { get; set; }
}