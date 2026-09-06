using DependencyModules.Runtime.Attributes;

namespace SqsTest.Impl;

[SingletonService]
public class CountingService {
    public int Count { get; set; }
}
