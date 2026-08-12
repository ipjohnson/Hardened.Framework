using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;

// The assembly rung of the three-level attribute chain. Several tests in this project assert that
// an attribute on a method or a class beats the same attribute declared here, so these are not
// incidental configuration — removing one turns the corresponding precedence test into a test of
// nothing, because both candidate answers become "not present".
//
// Only one EnvironmentValue is declared. EnvironmentValueAttribute does not set
// AttributeUsage.AllowMultiple, so a second one on the same target is a compile error even though
// the harness reads them as a list — see EnvironmentAttributeTests for the note.
[assembly: HardenedTestEntryPoint(typeof(AssemblyEntryPointModule))]
[assembly: EnvironmentName("assembly-environment")]
[assembly: EnvironmentValue("assembly-scoped-value", "from-assembly")]
