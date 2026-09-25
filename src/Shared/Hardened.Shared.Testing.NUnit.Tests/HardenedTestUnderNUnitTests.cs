using System.Reflection;
using DependencyModules.NUnit.Attributes;
using DependencyModules.Testing.Attributes;
using DependencyModules.Testing.Impl;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Hardened.Shared.Testing.NUnit.Tests;

/// <summary>
/// A <c>[ModuleTest]</c> reads the same under NUnit: the container is the entry point's, a
/// parameter is resolved from it, a <c>[Mock]</c> beats the application's registration, and
/// <see cref="CurrentTest"/> names the running test.
/// </summary>
public class HardenedTestUnderNUnitTests
{
    [ModuleTest]
    public void AParameterIsResolvedFromTheEntryPointsContainer(IGreetingService greeting)
    {
        Assert.That(greeting.Greet("world"), Is.EqualTo("real hello world"));
    }

    [ModuleTest]
    public void AMockBeatsTheApplicationsRegistration(
        [Mock] IGreetingService greeting,
        IServiceProvider provider
    )
    {
        greeting.Greet("world").Returns("substitute hello world");

        Assert.That(provider.GetRequiredService<IGreetingService>(), Is.SameAs(greeting));
        Assert.That(greeting.Greet("world"), Is.EqualTo("substitute hello world"));
    }

    [ModuleTest]
    public void TheEnvironmentIsTheOneTheAssemblyDeclares(
        IHardenedEnvironment environment,
        ITestContext context
    )
    {
        Assert.That(environment.Name, Is.EqualTo("nunit-environment"));
        Assert.That(context.Logger, Is.Not.Null);
    }

    [ModuleTest]
    public void TheRunningTestIsKeyedAndNamed()
    {
        Assert.That(
            CurrentTest.Provider?.GetType().Assembly,
            Is.SameAs(typeof(ModuleTestAttribute).Assembly),
            "the provider is DependencyModules.NUnit's"
        );
        Assert.That(CurrentTest.Key, Is.Not.Null);
        Assert.That(
            CurrentTest.Key,
            Is.SameAs(CurrentTest.Key),
            "one object per test, stable across reads"
        );
        Assert.That(CurrentTest.Assembly, Is.SameAs(typeof(HardenedTestUnderNUnitTests).Assembly));
        Assert.That(CurrentTest.DisplayName, Does.Contain(nameof(TheRunningTestIsKeyedAndNamed)));
    }

    /// <summary>The key flows through async code, as an assertion after an await needs it to.</summary>
    [ModuleTest]
    public async Task TheKeySurvivesAnAwait()
    {
        var before = CurrentTest.Key;

        await Task.Delay(1);

        Assert.That(CurrentTest.Key, Is.SameAs(before));
    }

    /// <summary>The application's log reaches NUnit's output for the test, as JSON.</summary>
    [ModuleTest]
    public void TheApplicationsLogIsWrittenToTheTestOutput(
        ILogger<HardenedTestUnderNUnitTests> logger
    )
    {
        logger.LogInformation("Order {OrderId} accepted", 42);

        Assert.That(
            TestExecutionContext.CurrentContext.CurrentResult.Output,
            Does.Contain("\"message\": \"Order 42 accepted\"")
        );
    }

    /// <summary>
    /// Outside a test NUnit answers an ad-hoc context with a placeholder test in it; the seam
    /// reports no test, which is what keeps a response recorded from a thread of the harness's
    /// own out of every test's <c>LastResponse</c>.
    /// </summary>
    [Test]
    public async Task OutsideARunningTestTheKeyIsNull()
    {
        Task<object?> outside;

        using (ExecutionContext.SuppressFlow())
        {
            outside = Task.Run(() => CurrentTest.Key);
        }

        Assert.That(await outside, Is.Null);
    }
}

/// <summary>
/// Two tests see two containers, and the first's is disposed by the time the second runs:
/// DependencyModules.NUnit disposes the provider in its own <c>finally</c> around the test.
/// </summary>
public class ContainerPerTestTests
{
    private static readonly object Sync = new();

    private static readonly List<object> EarlierKeys = [];

    private static readonly List<TrackedDisposable> EarlierDisposables = [];

    [ModuleTest]
    [TrackedDisposable]
    public void TheFirstOfTwo(TrackedDisposable tracked) => Check(tracked);

    [ModuleTest]
    [TrackedDisposable]
    public void TheSecondOfTwo(TrackedDisposable tracked) => Check(tracked);

    private static void Check(TrackedDisposable current)
    {
        lock (Sync)
        {
            Assert.That(current.Disposed, Is.False);

            foreach (var earlier in EarlierDisposables)
            {
                Assert.That(
                    earlier.Disposed,
                    Is.True,
                    "the earlier test's container was disposed when it finished"
                );
            }

            foreach (var earlier in EarlierKeys)
            {
                Assert.That(CurrentTest.Key, Is.Not.SameAs(earlier), "each test has its own key");
            }

            EarlierDisposables.Add(current);
            EarlierKeys.Add(CurrentTest.Key!);
        }
    }
}

public sealed class TrackedDisposable : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>Registers a <see cref="TrackedDisposable"/> the container creates, and so disposes.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TrackedDisposableAttribute
    : Attribute,
        IHardenedTestDependencyRegistrationAttribute
{
    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection
    )
    {
        serviceCollection.AddSingleton<TrackedDisposable>();
    }
}
