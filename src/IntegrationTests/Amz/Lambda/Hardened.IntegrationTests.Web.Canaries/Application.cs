using Hardened.Amz.Canaries.Runtime;
using Hardened.IntegrationTests.Web.Canaries;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Testing.Attributes;

[assembly: HardenedTestEntryPoint(typeof(Application))]

namespace Hardened.IntegrationTests.Web.Canaries;

[HardenedStartup]
public partial class Application
{
    private IEnumerable<IApplicationModule> Modules()
    {
        yield return new CanaryLibrary();
    }
}