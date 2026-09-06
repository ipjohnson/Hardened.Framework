using DependencyModules.Runtime.Attributes;

namespace DynamoDbStreamApp.Impl;

[SingletonService]
public class CountingService {
    public int Count { get; set; }
}
