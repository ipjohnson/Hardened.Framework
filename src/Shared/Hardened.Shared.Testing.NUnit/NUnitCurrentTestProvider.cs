using System.Reflection;
using Hardened.Shared.Testing.Logging;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Internal;

namespace Hardened.Shared.Testing;

/// <summary>
/// <see cref="CurrentTest"/> for NUnit, over <see cref="TestExecutionContext.CurrentContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// The execution context is NUnit's own ambient test: <c>AsyncLocal</c>-backed, so it flows
/// through async code, and its <see cref="TestExecutionContext.CurrentTest"/> is one object per
/// test that NUnit holds for the run. Outside a test NUnit answers an
/// <see cref="TestExecutionContext.AdhocContext"/> carrying a placeholder test rather than null,
/// which is read here as no test at all. <c>NUnit.Framework.TestContext</c> is not used for the
/// key because it is a fresh wrapper on every read.
/// </para>
/// <para>
/// Installed by the static constructor of <c>[HardenedTest]</c>, which the runner reads before it
/// builds that test's container - so every <c>[HardenedTest]</c> has the seam in place. A plain
/// test that reads the seam outside a <c>[HardenedTest]</c>, or a test project that drives the
/// harness directly and declares none, calls <see cref="Install"/> itself. Installing is
/// idempotent and never replaces a provider another package put there.
/// </para>
/// </remarks>
public sealed class NUnitCurrentTestProvider : ICurrentTestProvider {

    public object? Key => RunningTest;

    public Assembly? Assembly => RunningTest?.TypeInfo?.Assembly;

    public string? DisplayName => RunningTest?.FullName;

    public ILoggerProvider CreateLoggerProvider() => new NUnitLoggerProvider();

    /// <summary>Installs this provider unless one is already installed.</summary>
    public static void Install() => CurrentTest.Provider ??= new NUnitCurrentTestProvider();

    private static Test? RunningTest {
        get {
            var context = TestExecutionContext.CurrentContext;

            return context is TestExecutionContext.AdhocContext ? null : context.CurrentTest;
        }
    }
}
