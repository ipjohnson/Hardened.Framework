using Hardened.IntegrationTests.Sqs.SUT;

namespace Hardened.IntegrationTests.Sqs.Shared;

/// <summary>
/// An <see cref="IOrderStore"/> that remembers what it was given, and refuses what it was told to.
/// </summary>
/// <remarks>
/// One per test, registered into that test's own container, so nothing is shared and no fixture has
/// to reset anything. Hand-written rather than a substitute because the assertions read better
/// against a list than against a received-calls query, and because refusing a particular order is
/// behaviour rather than verification.
///
/// <para>
/// Linked into both suites that use it rather than copied, the way the conformance fixtures share
/// theirs. Two copies of a recorder drift, and the drift shows up as one suite passing on behaviour
/// the other no longer has.
/// </para>
/// </remarks>
public sealed class RecordingOrderStore : IOrderStore {
    private readonly HashSet<string> _refuse = new(StringComparer.Ordinal);

    public List<Order> Placed { get; } = [];

    public IEnumerable<string> Ids => Placed.Select(order => order.Id);

    /// <summary>Makes the handler throw for one order, for the failure paths.</summary>
    public RecordingOrderStore Refusing(params string[] ids) {
        foreach (var id in ids) {
            _refuse.Add(id);
        }

        return this;
    }

    public void Place(Order order) {
        Placed.Add(order);

        if (_refuse.Contains(order.Id)) {
            throw new InvalidOperationException("handler refused order " + order.Id);
        }
    }
}
