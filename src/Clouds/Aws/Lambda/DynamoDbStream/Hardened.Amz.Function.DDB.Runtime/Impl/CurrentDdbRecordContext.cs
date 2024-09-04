using Amazon.Lambda.DynamoDBEvents;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Function.DDB.Runtime;

[Expose]
[Singleton]
public class CurrentDdbRecordContext {
    public DynamoDBEvent.DynamodbStreamRecord CurrentRecord { get; set; } = default!;
}