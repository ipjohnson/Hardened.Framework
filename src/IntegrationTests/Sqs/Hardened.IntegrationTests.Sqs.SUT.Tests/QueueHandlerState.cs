using Xunit;

namespace Hardened.IntegrationTests.Sqs.SUT.Tests;

/// <summary>
/// The two fixtures share <c>OrderHandlers</c>' static record of what ran, so they cannot run at
/// the same time.
/// </summary>
/// <remarks>
/// The shared state is deliberate: the pair exists to compare two deployments of the same handlers,
/// and giving each its own would make them two unrelated fixtures. xUnit runs test classes in
/// parallel by default, so without this the two reset each other's state mid-invocation - which is
/// exactly how it failed the first time.
/// </remarks>
[CollectionDefinition(Name)]
public class QueueHandlerState {
    public const string Name = "queue handler state";
}
