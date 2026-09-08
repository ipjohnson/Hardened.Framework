using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Runtime.Tests.Dispatch;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// The harness for the tests that run MixedApp through the runner; the plain [Fact] classes in
// this assembly are untouched by these.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(MixedApp))]
