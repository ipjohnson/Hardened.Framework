using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.IntegrationTests.CloudRunTopic.SUT;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// [CloudRunTesting] is what makes each façade call an Eventarc CloudEvent on the topic, through
// the front door; [WebTesting] registers the host it goes to; [KestrelTesting] answers for
// [KestrelRuntime] on a class with a socket.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(CloudRunTopicApp))]
