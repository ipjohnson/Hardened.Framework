using DependencyModules.NSubstitute;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.AzureBlob.SUT;
using Hardened.Shared.Testing.Attributes;

// The pipeline rung's setup; see the queue fixture. No cloud is named.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(AzureBlobTestApp))]
