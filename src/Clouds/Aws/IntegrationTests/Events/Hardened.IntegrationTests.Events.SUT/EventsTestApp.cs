using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Events.SUT;

/// <summary>
/// One function serving a queue, a topic, a schedule and a bus.
/// </summary>
/// <remarks>
/// Nothing here names a module or an adapter. Four triggers across the handlers pull in three
/// modules - EventBridge serves both the schedule and the bus event - and because more than one
/// adapter is registered, this is the fixture where an arriving payload is actually asked about.
/// </remarks>
[HardenedModule]
public partial class EventsTestApp {
}
