using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Union.SUT;

[HardenedModule]
[HardenedWebModule]
[BasePath("/union")]
public partial class UnionLibrary;
