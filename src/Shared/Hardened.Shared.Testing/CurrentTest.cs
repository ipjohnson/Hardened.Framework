using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Hardened.Shared.Testing;

/// <summary>
/// The running test, as the test runner in use reports it.
/// </summary>
/// <remarks>
/// <para>
/// The harness keeps two things per running test - the last response the pipeline answered, and
/// what a generated client received - and both are read from a static, because that is the only
/// place an assertion written after the call can reach them. A static needs a key, and every
/// runner has one: xUnit's <c>TestContext.Current.Test</c>, NUnit's
/// <c>TestExecutionContext.CurrentContext.CurrentTest</c>. Each is one object per test, lives as
/// long as the test, and flows through async code. This is the seam that hands one of them to the
/// harness without the harness naming a runner.
/// </para>
/// <para>
/// The runner package installs its <see cref="ICurrentTestProvider"/> from the static constructor
/// of its <c>[HardenedTest]</c> - <c>Hardened.Shared.Testing.xUnit</c> for xUnit,
/// <c>Hardened.Shared.Testing.NUnit</c> for NUnit - which the runner instantiates while it
/// discovers tests, before any test runs. A test project references exactly one of the two; one
/// that drives the harness directly and declares no <c>[HardenedTest]</c> calls the provider's
/// <c>Install()</c> itself.
/// </para>
/// </remarks>
public static class CurrentTest {

    /// <summary>The installed provider, or null when no runner package has loaded.</summary>
    public static ICurrentTestProvider? Provider { get; set; }

    /// <summary>
    /// An object identifying the running test, which lives exactly as long as it and flows
    /// through async code. Null outside a test, and null when no runner package is installed.
    /// </summary>
    public static object? Key => Provider?.Key;

    /// <summary>The assembly the running test is declared in, or null outside a test.</summary>
    public static Assembly? Assembly => Provider?.Assembly;

    /// <summary>The running test's display name, for a failure that names it, or null outside a test.</summary>
    public static string? DisplayName => Provider?.DisplayName;
}

/// <summary>
/// What a runner package supplies to <see cref="CurrentTest"/>.
/// </summary>
public interface ICurrentTestProvider {

    /// <summary>See <see cref="CurrentTest.Key"/>.</summary>
    object? Key { get; }

    /// <summary>See <see cref="CurrentTest.Assembly"/>.</summary>
    Assembly? Assembly { get; }

    /// <summary>See <see cref="CurrentTest.DisplayName"/>.</summary>
    string? DisplayName { get; }

    /// <summary>
    /// A logger provider that writes a test's log lines where the runner shows them: xUnit's
    /// test output, NUnit's <c>TestContext.Out</c>. Registered by the entry point attribute in
    /// place of whatever the application's logging configured.
    /// </summary>
    ILoggerProvider CreateLoggerProvider();
}
