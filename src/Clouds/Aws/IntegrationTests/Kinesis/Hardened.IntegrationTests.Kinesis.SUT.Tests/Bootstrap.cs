using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Kinesis.SUT;
using Hardened.Shared.Testing.Attributes;

// [LambdaTesting] is what makes this a stream test rather than a pipeline one: each message is
// serialized and base64-encoded into a real Kinesis record and delivered through the invocation
// loop, so the peek, the decode and the batch fan-out all run.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(StreamTestApp))]
