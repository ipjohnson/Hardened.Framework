using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;
using SqsTest;

[assembly: HardenedTestEntryPoint(typeof(Application))]

// Registers the invoke filter provider and, at startup, calls IMiddlewareService.Use to put the
// invoke filter into the chain. Without it MiddlewareService holds no filters at all, so
// InvokeFunction builds an execution chain of length zero, returns an empty stream, and the
// handler never runs - no error anywhere.
[assembly: LambdaFunctionTesting]
