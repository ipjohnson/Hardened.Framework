using Hardened.IntegrationTests.RegisteredRoutes.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(RegisteredRoutesLibrary))]
