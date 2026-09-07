using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.S3.SUT;
using Hardened.Shared.Testing.Attributes;

// [LambdaTesting] is what makes this a blob test rather than a pipeline one: each message becomes a
// real S3 notification with the key form-encoded the way S3 encodes it, delivered through the
// invocation loop - so the peek, the key decode and the batch fan-out all run.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(BlobTestApp))]
