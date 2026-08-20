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
    private readonly List<Order> _orders = [];

    public IReadOnlyList<Order> Orders => _orders;

    public void Record(Order order) => _orders.Add(order);
}
