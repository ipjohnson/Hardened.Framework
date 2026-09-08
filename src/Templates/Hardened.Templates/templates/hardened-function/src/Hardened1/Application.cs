#if (gcp)
using Hardened.Gcp.CloudRun.Runtime;
#endif
using Hardened.Shared.Runtime.Attributes;

namespace Hardened1;

#if (aws)
/// <summary>
/// The application. What it runs on is not written here.
/// </summary>
/// <remarks>
/// <b>There is no host module attribute, and that is the point.</b> The adapter, its serializer
/// and the filters it needs all arrive because the handler carries a trigger attribute: the
/// generator reads the build property the host package declares and registers the module for you.
/// Nothing in this file names a cloud, so moving to another one is a package reference.
///
/// partial is not optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
public partial class Application;
#endif
#if (gcp)
/// <summary>
/// The application. The host is the one thing it names.
/// </summary>
/// <remarks>
/// <b>There is no adapter module attribute here, and that is the point.</b> The envelope the front
/// door recognises and the filters it needs arrive because the handler carries a trigger
/// attribute: the generator reads the build property the adapter package declares and registers
/// the module for you. What this file does name is the host, the way a web application names
/// [KestrelRuntime], because a Cloud Run service is a container listening on a port and that is a
/// fact about the deployment. Moving to another cloud changes this attribute and a package
/// reference.
///
/// partial is not optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
[CloudRunRuntime]
public partial class Application;
#endif
