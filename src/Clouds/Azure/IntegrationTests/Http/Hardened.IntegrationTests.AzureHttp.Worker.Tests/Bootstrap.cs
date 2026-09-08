using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureHttp.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

// The pipeline rung's setup plus one line. [AzureFunctionsWebTesting] replaces the host with the
// one that builds the worker's request data and calls the invocation handler; the linked
// HttpFunctionTests.cs does not know.
[assembly: WebTesting]
[assembly: AzureFunctionsWebTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureHttpTestApp))]
