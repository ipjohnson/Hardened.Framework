using Hardened.Shared.Testing.Impl;
using Xunit;
using Xunit.Sdk;

namespace Hardened.Shared.Testing.Attributes;

[XunitTestCaseDiscoverer("Hardened.Shared.Testing.Impl." + nameof(HardenedTestDiscoverer), "Hardened.Shared.Testing")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HardenedTestAttribute : FactAttribute { }