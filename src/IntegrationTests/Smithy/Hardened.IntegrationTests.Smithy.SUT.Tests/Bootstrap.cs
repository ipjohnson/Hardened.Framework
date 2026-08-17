using Hardened.IntegrationTests.Smithy.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(SmithyTestApp))]
