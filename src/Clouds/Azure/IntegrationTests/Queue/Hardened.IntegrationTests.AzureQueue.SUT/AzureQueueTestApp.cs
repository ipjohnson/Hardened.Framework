using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureQueue.SUT;

/// <summary>
/// The entry point a queue function is anchored on.
/// </summary>
/// <remarks>
/// <b>There is no Service Bus module attribute here, and that is the point.</b> The adapter and the
/// batch fan-out filter arrive because a handler in this project wrote <c>[Queue]</c>: the library
/// generator reads <c>HardenedQueueModule</c> off the adapter package's build properties and adds
/// the module, and the Azure generator reads the same property and writes the <c>[Function]</c>
/// the host invokes. Nothing in this file names Azure, so the same application on Lambda changes a
/// package reference and nothing else - <c>SqsTestApp</c> is this file with another name.
/// </remarks>
[HardenedModule]
public partial class AzureQueueTestApp {
}
