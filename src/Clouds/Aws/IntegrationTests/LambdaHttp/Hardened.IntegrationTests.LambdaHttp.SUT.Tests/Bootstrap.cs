using Hardened.Aws.Lambda.Testing;
using Hardened.IntegrationTests.LambdaHttp.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;

// [WebTesting] is the harness every web suite uses; [LambdaWebTesting] is the only line that says
// these run on Lambda. Remove it and the same tests run on the pipeline; swap it for
// [KestrelTesting] and they run over a socket. That is the portability claim as something a build
// can check rather than something a document asserts.
[assembly: WebTesting]
[assembly: LambdaWebTesting]
[assembly: HardenedTestEntryPoint(typeof(LambdaHttpTestApp))]
