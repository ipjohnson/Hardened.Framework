using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using DependencyModules.xUnit.Attributes;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Shared.Testing.Tests;

/// <summary>
/// The running-test seam as the harness reads it: what the xUnit provider answers, and the logger
/// the entry point attribute registers with a provider installed and without one.
/// </summary>
/// <remarks>
/// <para>
/// DependencyModules.xUnit installs its provider from the static constructor of
/// <see cref="ModuleTestAttribute"/>. The tests here do not depend on xUnit having read a
/// <c>[ModuleTest]</c> before they run, so this class runs that constructor itself.
/// </para>
/// <para>
/// The provider is process-wide, and two tests here replace it. They run in a collection that
/// disables parallelization, which xUnit runs alone rather than beside the other classes: in CI
/// another class installed its provider inside the window one of them had opened, and the
/// container it built for the no-runner case came out with a logger provider after all.
/// </para>
/// </remarks>
[Collection(SeamCollection.Name)]
public class CurrentTestTests
{
    static CurrentTestTests() =>
        RuntimeHelpers.RunClassConstructor(typeof(ModuleTestAttribute).TypeHandle);

    [Fact]
    public void InsideATestTheXunitProviderNamesIt()
    {
        Assert.Same(typeof(ModuleTestAttribute).Assembly, CurrentTest.Provider?.GetType().Assembly);
        Assert.NotNull(CurrentTest.Key);
        Assert.Same(typeof(CurrentTestTests).Assembly, CurrentTest.Assembly);
        Assert.Contains(nameof(InsideATestTheXunitProviderNamesIt), CurrentTest.DisplayName);
    }

    /// <summary>
    /// With no runner package loaded - the entry point attribute driven from a test of its own -
    /// the attribute registers no logger provider rather than a console one nobody reads.
    /// </summary>
    [Fact]
    public void WithNoProviderNoLoggerProviderIsRegistered()
    {
        var installed = CurrentTest.Provider;

        CurrentTest.Provider = null;

        try
        {
            var collection = new ServiceCollection();

            new HardenedTestEntryPointAttribute(
                typeof(AssemblyEntryPointModule)
            ).SetupServiceCollection(
                FakeTestMethodContext.For<Target>(nameof(Target.Method)),
                collection
            );

            Assert.DoesNotContain(
                collection,
                descriptor => descriptor.ServiceType == typeof(ILoggerProvider)
            );
        }
        finally
        {
            CurrentTest.Provider = installed;
        }
    }

    /// <summary>
    /// The application's log reaches the running test's output through the installed provider,
    /// one JSON record per entry.
    /// </summary>
    [Fact]
    public void WithAProviderTheApplicationsLogIsWrittenToTheTestOutputAsJson()
    {
        var installed = CurrentTest.Provider;
        var fake = new RecordingProvider();

        CurrentTest.Provider = fake;

        try
        {
            var collection = new ServiceCollection();

            new HardenedTestEntryPointAttribute(
                typeof(AssemblyEntryPointModule)
            ).SetupServiceCollection(
                FakeTestMethodContext.For<Target>(nameof(Target.Method)),
                collection
            );

            using var provider = collection.BuildServiceProvider();

            provider
                .GetRequiredService<ILogger<CurrentTestTests>>()
                .LogWarning("Order {OrderId} rejected", 42);

            using var entry = JsonDocument.Parse(Assert.Single(fake.Lines));

            Assert.Equal(
                typeof(CurrentTestTests).FullName,
                entry.RootElement.GetProperty("logger").GetString()
            );
            Assert.Equal("Warning", entry.RootElement.GetProperty("logLevel").GetString());
            Assert.Equal("Order 42 rejected", entry.RootElement.GetProperty("message").GetString());
        }
        finally
        {
            CurrentTest.Provider = installed;
        }
    }

    private class Target
    {
        public void Method() { }
    }

    private sealed class RecordingProvider : ICurrentTestProvider
    {
        public List<string> Lines { get; } = [];

        public object? Key { get; } = new();

        public string? DisplayName => "a test";

        public Assembly? Assembly => typeof(CurrentTestTests).Assembly;

        public bool TryWriteLine(string message)
        {
            Lines.Add(message);

            return true;
        }
    }
}

/// <summary>Runs alone: nothing else in the assembly runs while a test in it holds the seam.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SeamCollection
{
    public const string Name = "the running-test seam";
}
