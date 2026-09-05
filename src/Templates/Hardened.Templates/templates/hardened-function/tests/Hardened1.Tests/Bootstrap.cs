#if (nsubstitute)
using DependencyModules.NSubstitute;
#endif
#if (moq)
using DependencyModules.Moq;
#endif
#if (fakeiteasy)
using DependencyModules.FakeItEasy;
#endif
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

// The mock library. [Mock] on a parameter asks this attribute for the double and builds nothing
// itself, so without it a [Mock] parameter fails with "Mock library not found". The package that
// carries it is in Hardened1.Tests.csproj.
#if (nsubstitute)
[assembly: NSubstituteSupport]
#endif
#if (moq)
[assembly: MoqSupport]
#endif
#if (fakeiteasy)
[assembly: FakeItEasySupport]
#endif
