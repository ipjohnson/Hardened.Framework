using System.Reflection;
using Hardened.Shared.Testing.Logging;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.v3;

namespace Hardened.Shared.Testing;

/// <summary>
/// <see cref="CurrentTest"/> for xUnit v3, over <see cref="TestContext.Current"/>.
/// </summary>
/// <remarks>
/// Installed by the static constructor of <c>[HardenedTest]</c>, which the runner reads before it
/// builds that test's container - so every <c>[HardenedTest]</c> has the seam in place. A plain
/// test in the same assembly can run before any <c>[HardenedTest]</c> has been read, and a test
/// project that drives the harness directly declares none, so a test that reads the seam
/// outside a <c>[HardenedTest]</c> calls <see cref="Install"/> itself. Installing is idempotent
/// and never replaces a provider another package put there.
/// </remarks>
public sealed class XunitCurrentTestProvider : ICurrentTestProvider {

    /// <summary>
    /// The running test, which xUnit holds for exactly as long as the test lives. Null while the
    /// container is being built: the DependencyModules runner does that in xUnit's test-method
    /// stage, where the context has the method and neither a test nor a test case yet.
    /// </summary>
    public object? Key => TestContext.Current.Test;

    public Assembly? Assembly =>
        TestContext.Current.TestClass is IXunitTestClass { Class: var testClass } ? testClass.Assembly : null;

    public string? DisplayName => TestContext.Current.Test?.TestDisplayName;

    public ILoggerProvider CreateLoggerProvider() => new XunitLoggerProvider();

    /// <summary>Installs this provider unless one is already installed.</summary>
    public static void Install() => CurrentTest.Provider ??= new XunitCurrentTestProvider();
}
