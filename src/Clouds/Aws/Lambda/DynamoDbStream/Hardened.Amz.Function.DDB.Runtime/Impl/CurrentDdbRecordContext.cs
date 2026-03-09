using Amazon.Lambda.DynamoDBEvents;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Function.DDB.Runtime.Impl;

[Expose]
[SingletonService]
public class CurrentDdbRecordContext {
    public DynamoDBEvent.DynamodbStreamRecord CurrentRecord { get; set; } = default!;
}