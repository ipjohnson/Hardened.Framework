using Hardened.Amz.Function.Lambda.Testing;
using Hardened.Shared.Testing.Attributes;
using Hardened1;

// The application under test. The real module graph is applied and startup services run, so there
// is no separate test wiring to keep in step.
[assembly: HardenedTestEntryPoint(typeof(Application))]

// Registers the invoke filter provider and, at startup, puts the invoke filter into the chain.
// Without it the pipeline holds no filters at all, so an invocation builds a chain of length zero,
// returns an empty stream and never reaches the handler - with no error anywhere.
[assembly: LambdaFunctionTesting]
