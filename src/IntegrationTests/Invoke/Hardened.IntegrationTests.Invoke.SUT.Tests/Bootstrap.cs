using DependencyModules.NSubstitute;
using Hardened.IntegrationTests.Invoke.SUT;
using Hardened.Shared.Testing.Attributes;

// No trigger harness here: a direct invocation has no source to send from, so there is no façade
// and the test hands the caller's own payload to the invocation loop.
[assembly: NSubstituteSupport]
[assembly: HardenedTestEntryPoint(typeof(InvokeTestApp))]
