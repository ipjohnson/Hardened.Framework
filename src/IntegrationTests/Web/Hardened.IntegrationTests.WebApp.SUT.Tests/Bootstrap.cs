using Hardened.IntegrationTests.WebApp.SUT;
using Hardened.Kiota.Testing;
using Hardened.Refit.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.AspNetCore.Testing;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]

// The two client routes. Every Kiota client and every Refit interface in this assembly is a test
// parameter after these, built over the pipeline with nothing written per client; a factory in
// TestClients.cs still wins for the client it names.
[assembly: KiotaTesting]
[assembly: RefitTesting]

// The two hosts. After these, [KestrelRuntime] or [AspNetCoreRuntime] on a test - the attribute
// the application names its own host with - runs that test on a real socket.
[assembly: KestrelTesting]
[assembly: AspNetCoreTesting]
