using Hardened.Shared.Runtime.Attributes;

namespace DynamoDbStreamApp.Impl;

[Expose]
[Singleton]
public class CountingService {
    public int Count { get; set; }
}