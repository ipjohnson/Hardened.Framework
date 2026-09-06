using Amazon.Lambda.DynamoDBEvents;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Function.DDB.Runtime.Impl;

[SingletonService]
public class CurrentDdbRecordContext {
    public DynamoDBEvent.DynamodbStreamRecord CurrentRecord { get; set; } = default!;
}