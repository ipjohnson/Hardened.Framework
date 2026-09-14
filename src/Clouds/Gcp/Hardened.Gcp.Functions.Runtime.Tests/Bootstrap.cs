using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Kestrel.Testing;
using Hardened.Web.Testing;

// The harness for the tests that run FunctionsApp through the pipeline host, which is the arm the
// Cloud Functions deliveries are compared against. The plain [Fact] classes are untouched by these.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: CloudRunTesting]
[assembly: WebTesting]
[assembly: KestrelTesting]
[assembly: HardenedTestEntryPoint(typeof(Hardened.Gcp.Functions.Runtime.Tests.FunctionsApp))]
