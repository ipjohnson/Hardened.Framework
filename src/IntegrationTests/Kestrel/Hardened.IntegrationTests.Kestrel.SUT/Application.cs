using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Kestrel.SUT;

/// <summary>
/// The application module. <c>KestrelRuntime</c> in place of <c>AspNetCoreRuntime</c> is the only
/// difference from an ASP.NET-hosted Hardened app — the controllers, filters and generated
/// routing are identical.
/// </summary>
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
public partial class Application { }
