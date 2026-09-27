using DependencyModules.NSubstitute;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;

// The assembly rung of the three-level attribute chain. Several tests in this project assert that
// an attribute on a method or a class beats the same attribute declared here, so these are not
// incidental configuration — removing one turns the corresponding precedence test into a test of
// nothing, because both candidate answers become "not present".
[assembly: HardenedTestEntryPoint(typeof(AssemblyEntryPointModule))]
[assembly: EnvironmentName("assembly-environment")]
[assembly: EnvironmentValue("assembly-scoped-value", "from-assembly")]

// The mock library. [Mock] is DependencyModules.Testing's and builds nothing itself: it asks the
// support attribute in scope, and without one a [Mock] parameter fails with "Mock library not found".
[assembly: NSubstituteSupport]
