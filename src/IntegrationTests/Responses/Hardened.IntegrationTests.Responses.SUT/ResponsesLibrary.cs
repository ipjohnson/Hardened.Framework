using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Responses.SUT;

[HardenedModule]
[HardenedWebModule]
[BasePath("/responses")]
public partial class ResponsesLibrary;
