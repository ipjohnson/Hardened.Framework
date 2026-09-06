using DynamoDbStreamApp;
using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;

[assembly: HardenedTestEntryPoint(typeof(Application))]

// Registers the invoke filter provider and, at startup, calls IMiddlewareService.Use to put the
// invoke filter into the chain. Without it MiddlewareService holds no filters, so InvokeFunction
// builds a chain of length zero and returns an empty stream - the handler never runs and nothing
// throws. See SqsTest.Tests/TestBootstrap.cs, which had the same gap.
[assembly: LambdaFunctionTesting]
