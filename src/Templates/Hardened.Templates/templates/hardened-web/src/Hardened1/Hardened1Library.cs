using Hardened.Shared.Runtime.Attributes;
#if (codeFirst)
using Hardened.Web.Runtime.Attributes;
#endif
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
#if (codeFirst)
// This assembly's URL space. Every route below it is relative to this.
[BasePath("/greeting")]
#endif
public partial class Hardened1Library;
