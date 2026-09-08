using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.CloudRunQueue.SUT;

/// <summary>
/// The entry point a queue service is anchored on.
/// </summary>
/// <remarks>
/// <b>There is no Pub/Sub module attribute here, and that is the point.</b> The envelope and the
/// batch fan-out filter arrive because a handler in this project wrote <c>[Queue]</c>: the
/// generator reads <c>HardenedQueueModule</c> off the adapter package's build properties and adds
/// the module to <c>DependencyRegistry&lt;CloudRunQueueApp&gt;</c>. What this file does name is
/// the host, the way a web application names <c>[KestrelRuntime]</c>, and the same application
/// moved to another provider changes that attribute and a package reference and nothing else.
/// </remarks>
[HardenedModule]
[CloudRunRuntime]
public partial class CloudRunQueueApp {
}
