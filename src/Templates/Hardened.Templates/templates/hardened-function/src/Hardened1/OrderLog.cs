using DependencyModules.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// A service, registered where it is declared rather than by a line in the module.
/// </summary>
/// <remarks>
/// It is here so the handler has a dependency worth injecting and the tests have something to
/// assert against. Replace it with whatever the function actually talks to.
/// </remarks>
[SingletonService]
public class OrderLog {
#if (blob)
    private readonly List<Upload> _uploads = [];

    public IReadOnlyList<Upload> Uploads => _uploads;

    public void Record(Upload upload) => _uploads.Add(upload);
#else
    private readonly List<Order> _orders = [];

    public IReadOnlyList<Order> Orders => _orders;

    public void Record(Order order) => _orders.Add(order);
#endif
#if (timer)

    public int Sweeps { get; private set; }

    /// <summary>What the schedule does. A timer carries no payload, so this takes none.</summary>
    public void Sweep() => Sweeps++;
#endif
}
