using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Sqs.SUT;
using Hardened.Shared.Testing.Attributes;

// The whole of the harness setup. [FunctionTesting] makes the generated façades resolvable,
// [LambdaTesting] raises the delivery to a real SQS envelope through the invocation loop, and
// [HardenedTestEntryPoint] names the application whose container every test runs against.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(SqsTestApp))]
