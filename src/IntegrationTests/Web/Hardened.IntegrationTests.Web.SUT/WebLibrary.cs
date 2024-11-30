using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT;

[HardenedModule]
[BasePath("/web-library")]
public partial class WebLibrary {
    public string Test { get; set; } = "Default";
}