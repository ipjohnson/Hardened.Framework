using Hardened.IntegrationTests.Union.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(UnionLibrary))]
