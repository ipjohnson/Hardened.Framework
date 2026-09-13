using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.RegisteredRoutes.SUT;

/// <summary>
/// An application whose routes are partly written and partly registered at startup.
/// </summary>
/// <remarks>
/// The base path is here to prove one thing: a registered path is composed onto it exactly as an
/// attribute route is. There is one routing system, and a module mounted at <c>/registered</c>
/// serves everything it declares under it however the declaration was written.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[BasePath("/registered")]
public partial class RegisteredRoutesLibrary;
