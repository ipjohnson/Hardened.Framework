using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.LambdaHttp.SUT;

/// <summary>
/// An HTTP application, which happens to be deployed as a Lambda.
/// </summary>
/// <remarks>
/// <c>[HardenedWebModule]</c> brings the routing table and the web pipeline, exactly as it would for
/// Kestrel. Nothing here names a front door: the verbs on the controller bind
/// <c>HardenedHttpModule</c>, and which module that is comes from the runtime package this project
/// references. Swapping that reference is the whole of moving these handlers to another host.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
public partial class LambdaHttpTestApp {
}
