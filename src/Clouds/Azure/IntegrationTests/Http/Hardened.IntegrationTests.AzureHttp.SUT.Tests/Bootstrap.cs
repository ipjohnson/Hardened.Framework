using Hardened.IntegrationTests.AzureHttp.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

// [WebTesting] is the harness every web suite uses, and at this rung it is the whole of the setup:
// the same tests run on the pipeline. The rung above adds [AzureFunctionsWebTesting], which is the
// only line that says they run behind an HTTP trigger; swap it for [KestrelTesting] and they run
// over a socket. That is the portability claim as something a build can check rather than
// something a document asserts.
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureHttpTestApp))]
