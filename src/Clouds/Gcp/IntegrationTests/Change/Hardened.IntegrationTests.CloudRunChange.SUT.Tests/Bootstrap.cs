using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunChange.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// [CloudRunTesting] is what makes this a change feed test rather than a pipeline one: the message
// is marshalled into a document's typed values and delivered as the protobuf event Eventarc
// sends, so the envelope and its value conversion run for real.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(CloudRunChangeApp))]
