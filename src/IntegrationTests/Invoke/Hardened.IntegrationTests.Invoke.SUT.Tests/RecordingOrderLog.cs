using Hardened.IntegrationTests.Invoke.SUT;

namespace Hardened.IntegrationTests.Invoke.SUT.Tests;

/// <summary>What the handler was asked to place, for one test.</summary>
public sealed class RecordingOrderLog : IOrderLog {
    public List<OrderRequest> Requests { get; } = [];

    public void Placed(OrderRequest request) => Requests.Add(request);
}
