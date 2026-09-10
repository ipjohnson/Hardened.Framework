using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Web.SUT;

/// <summary>
/// A library module that declares a filter for every handler compiled with it.
/// </summary>
/// <remarks>
/// <c>[ConditionalGet]</c> here rather than in the host, because the rung is the compilation: this
/// covers <c>SomeController</c> and reaches none of the host's own handlers, which is what
/// <c>ModuleDeclaredFilterTests</c> asserts from both sides.
/// </remarks>
[HardenedModule]
[BasePath("/web-library")]
[HardenedWebModule]
[ConditionalGet]
public partial class WebLibrary {
    public string Test { get; set; } = "Default";
}