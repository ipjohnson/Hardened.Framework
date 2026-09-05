using DependencyModules.NSubstitute;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.NUnit.Tests;

// The module under test and the environment its tests declare, the way an application's test
// project declares them. Nothing here names a runner: [HardenedTest] is the only thing that does,
// and it comes from Hardened.Shared.Testing.NUnit.
[assembly: HardenedTestEntryPoint(typeof(GreetingModule))]
[assembly: EnvironmentName("nunit-environment")]

// The mock library [Mock] asks for its double. The attribute is DependencyModules.Testing's, the
// same under either runner; only this line says which library answers.
[assembly: NSubstituteSupport]
