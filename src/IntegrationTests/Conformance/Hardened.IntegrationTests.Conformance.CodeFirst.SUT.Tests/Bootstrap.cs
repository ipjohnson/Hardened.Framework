using Hardened.IntegrationTests.Conformance.CodeFirst.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(CodeFirstTestApp))]
