using DependencyModules.NSubstitute;
using Hardened.Azure.Functions.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Hardened.Shared.Testing.Attributes;

// The pipeline rung's setup plus one line. [AzureFunctionsTesting] replaces the delivery with the
// one that builds the worker's trigger data and binding data and calls the invocation handler; the
// linked QueueTests.cs does not know.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: AzureFunctionsTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureQueueTestApp))]
