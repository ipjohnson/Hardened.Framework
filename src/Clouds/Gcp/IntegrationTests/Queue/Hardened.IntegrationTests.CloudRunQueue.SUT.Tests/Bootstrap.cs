using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunQueue.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// The whole of the harness setup. [FunctionTesting] makes the generated façades resolvable,
// [CloudRunTesting] raises the delivery to a real Pub/Sub push through the front door,
// [WebTesting] registers the host that push goes to, [KestrelTesting] answers for [KestrelRuntime]
// on a class with a socket, and [HardenedTestEntryPoint] names the application whose container
// every test runs against.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(CloudRunQueueApp))]
