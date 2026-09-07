using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Shared.Testing.Attributes;

// [LambdaTesting] is what makes each façade call build the envelope its source actually sends, so
// these tests go through the adapter and the peek rather than straight into the pipeline.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(EventsTestApp))]
