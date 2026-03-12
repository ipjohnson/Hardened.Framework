using DependencyModules.xUnit.Impl;
using Xunit;
using Xunit.v3;

namespace Hardened.Shared.Testing.Attributes;

[XunitTestCaseDiscoverer(typeof(ModuleTestDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedTestAttribute : FactAttribute { }
