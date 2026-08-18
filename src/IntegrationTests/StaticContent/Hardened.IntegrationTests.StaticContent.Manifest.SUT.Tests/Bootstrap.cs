using Hardened.IntegrationTests.StaticContent.Manifest.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
