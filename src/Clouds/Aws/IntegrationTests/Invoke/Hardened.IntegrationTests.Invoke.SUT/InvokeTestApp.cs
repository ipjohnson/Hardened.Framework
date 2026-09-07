using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Invoke.SUT;

/// <summary>
/// A directly invoked function.
/// </summary>
/// <remarks>
/// No trigger attribute anywhere in this project, so nothing registers an event adapter and the
/// invoke adapter is the only one present - which is required rather than convenient. A caller's
/// payload need not be JSON at all, so a function that had to ask an adapter about it would have to
/// parse something that may not parse.
/// </remarks>
[HardenedModule]
public partial class InvokeTestApp {
}
