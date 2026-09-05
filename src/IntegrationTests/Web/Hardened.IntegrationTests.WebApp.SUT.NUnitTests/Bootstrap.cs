using DependencyModules.NSubstitute;
using Hardened.IntegrationTests.WebApp.SUT;
using Hardened.Kiota.Testing;
using Hardened.Refit.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.AspNetCore.Testing;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// The same four lines the xUnit project declares. Nothing here names a runner; [HardenedTest]
// does, and here it is Hardened.Shared.Testing.NUnit's.
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]

// The mock library [Mock] asks for its double. The attribute is DependencyModules.Testing's, the
// same under either runner; only this line says which library answers.
[assembly: NSubstituteSupport]
[assembly: KiotaTesting]
[assembly: RefitTesting]
[assembly: KestrelTesting]
[assembly: AspNetCoreTesting]
