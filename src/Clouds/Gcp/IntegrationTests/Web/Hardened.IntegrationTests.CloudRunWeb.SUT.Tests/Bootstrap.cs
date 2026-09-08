using Hardened.IntegrationTests.CloudRunWeb.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// [WebTesting] is the harness every web suite uses; [KestrelTesting] answers for [KestrelRuntime]
// on a class with a socket. Nothing here names Google: the application's own attribute does.
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(CloudRunWebApp))]
