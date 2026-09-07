using DependencyModules.NSubstitute;
using Hardened.Aws.Lambda.Testing;
using Hardened.Functions.Testing;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.Shared.Testing.Attributes;

// [LambdaTesting] puts the invocation loop and the invoke adapter in the path, so a call through
// the façade is what a caller of the deployed function would get back.
[assembly: NSubstituteSupport]
[assembly: FunctionTesting]
[assembly: LambdaTesting]
[assembly: HardenedTestEntryPoint(typeof(InvokeTestApp))]
