using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened1;

/// <summary>
/// The library module: this assembly's handlers, services and URL space.
/// </summary>
/// <remarks>
/// The host imports it with the single generated [Hardened1Library] attribute. partial is not
/// optional - the generator writes the other half.
/// </remarks>
[HardenedModule]
[HardenedWebModule]
[BasePath("/greeting")]
public partial class Hardened1Library { }
