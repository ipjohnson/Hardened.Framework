using DependencyModules.NSubstitute;
using Hardened.Azure.Functions.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.AzureStream.SUT;
using Hardened.Shared.Testing.Attributes;

// The pipeline rung's setup plus one line; see the queue fixture's worker project.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: AzureFunctionsTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureStreamTestApp))]
