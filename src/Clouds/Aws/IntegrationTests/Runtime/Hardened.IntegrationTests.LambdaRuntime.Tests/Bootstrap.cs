using DependencyModules.NSubstitute;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Testing.Attributes;

// No trigger harness: these tests drive the function through the Runtime API rather than through a
// façade, which is the whole point of the suite.
[assembly: NSubstituteSupport]
[assembly: HardenedTestEntryPoint(typeof(SqsTestApp))]
