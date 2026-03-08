using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Web.SUT;

[HardenedModule]
[BasePath("/web-library")]
[HardenedWebModule]
public partial class WebLibrary {
    public string Test { get; set; } = "Default";
}