using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Hardened.Shared.Testing.Attributes;

// The whole of the harness setup at the pipeline rung. [FunctionTesting] makes the generated
// façades resolvable and delivers through the pipeline, and [HardenedTestEntryPoint] names the
// application whose container every test runs against. No cloud is named: the same file with
// [AzureFunctionsTesting] added is the rung above.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureQueueTestApp))]
