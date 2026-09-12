using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.AzureHttp.SUT;

/// <summary>
/// An HTTP application, which happens to be deployed as a function.
/// </summary>
/// <remarks>
/// <c>[HardenedWebModule]</c> brings the routing table and the web pipeline, exactly as it would
/// for Kestrel. Nothing here names Azure: the verbs on the controller bind
/// <c>HardenedHttpModule</c>, and which module that is comes from the runtime package this project
/// references. Swapping that reference is the whole of moving these handlers to another host -
/// <c>LambdaHttpTestApp</c> is this file with another name.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
public partial class AzureHttpTestApp {
}
