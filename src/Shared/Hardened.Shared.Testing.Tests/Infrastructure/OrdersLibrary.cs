using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Shared.Testing.Tests.Infrastructure;

/// <summary>
/// A library module as the <c>hardened-library</c> template writes it: generated, and naming no
/// host, so nothing in its own graph imports <c>HardenedCoreModule</c>.
/// </summary>
[HardenedModule]
public partial class OrdersLibrary;

[ConfigurationModel]
public partial class OrdersOptions
{
    [FromEnvironmentVariable("ORDERS_QUEUE_NAME")]
    private string _queueName = "orders";
}
