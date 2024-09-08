using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Web.SUT;

[HardenedModule]
public partial class WebLibrary {
    public string Test { get; set; } = "Default";
}