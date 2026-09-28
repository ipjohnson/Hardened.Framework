using Hardened.IntegrationTests.Jwt.SUT;
using Hardened.Requests.Jwt.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: JwtTestIssuer]
[assembly: HardenedTestEntryPoint(typeof(JwtApp))]
