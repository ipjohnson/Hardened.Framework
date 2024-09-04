using Hardened.Shared.Runtime.Attributes;

namespace SqsTest.Impl;

[Expose]
[Singleton]
public class CountingService {
    public int Count { get; set; }
}