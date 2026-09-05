using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.NUnit.Tests;

// The module under test and the environment its tests declare, the way an application's test
// project declares them. Nothing here names a runner: [HardenedTest] is the only thing that does,
// and it comes from Hardened.Shared.Testing.NUnit.
[assembly: HardenedTestEntryPoint(typeof(GreetingModule))]
[assembly: EnvironmentName("nunit-environment")]
