using System.Reflection;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Shared.Testing.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Shared.Testing.Tests;

/// <summary>
/// The running-test seam: what it answers with a provider installed, without one, and what the
/// entry point attribute registers in each case.
/// </summary>
/// <remarks>
/// <para>
/// The runner installs the provider when it reads a <c>[HardenedTest]</c>, which it does before
/// that test's container is built, and a plain test in the same assembly can run before any has
/// been read - which this class found: the seam was still empty in a <c>[Fact]</c>. So the tests
/// that read it install it first, the way a test project that drives the harness directly does.
/// </para>
/// <para>
/// The provider is process-wide, so the two tests that replace it put the xUnit one back in a
/// <c>finally</c> and keep the window short; a container built in that window by another test
/// gets no logger provider, which is not a failure of that test.
/// </para>
/// </remarks>
public class CurrentTestTests {

    static CurrentTestTests() => XunitCurrentTestProvider.Install();

    [Fact]
    public void InsideATestTheXunitProviderNamesIt() {
        Assert.IsType<XunitCurrentTestProvider>(CurrentTest.Provider);
        Assert.NotNull(CurrentTest.Key);
        Assert.Same(typeof(CurrentTestTests).Assembly, CurrentTest.Assembly);
        Assert.Contains(nameof(InsideATestTheXunitProviderNamesIt), CurrentTest.DisplayName);
    }

    [Fact]
    public void WhatTheInstalledProviderAnswersIsWhatTheSeamAnswers() {
        var installed = CurrentTest.Provider;
        var fake = new FakeProvider();

        CurrentTest.Provider = fake;

        try {
            Assert.Same(fake.Key, CurrentTest.Key);
            Assert.Same(fake.Assembly, CurrentTest.Assembly);
            Assert.Equal("a test", CurrentTest.DisplayName);
        }
        finally {
            CurrentTest.Provider = installed;
        }
    }

    /// <summary>
    /// With no runner package loaded - the entry point attribute driven from a test of its own -
    /// the seam answers nothing and the attribute registers no logger provider rather than a
    /// console one nobody reads.
    /// </summary>
    [Fact]
    public void WithNoProviderTheSeamIsEmptyAndNoLoggerProviderIsRegistered() {
        var installed = CurrentTest.Provider;

        CurrentTest.Provider = null;

        try {
            Assert.Null(CurrentTest.Key);
            Assert.Null(CurrentTest.Assembly);
            Assert.Null(CurrentTest.DisplayName);

            var collection = new ServiceCollection();

            new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
                .SetupServiceCollection(FakeTestMethodContext.For<Target>(nameof(Target.Method)), collection);

            Assert.DoesNotContain(collection, descriptor => descriptor.ServiceType == typeof(ILoggerProvider));
        }
        finally {
            CurrentTest.Provider = installed;
        }
    }

    [Fact]
    public void WithTheProviderInstalledTheEntryPointRegistersItsLoggerProvider() {
        var collection = new ServiceCollection();

        XunitCurrentTestProvider.Install();

        new HardenedTestEntryPointAttribute(typeof(AssemblyEntryPointModule))
            .SetupServiceCollection(FakeTestMethodContext.For<Target>(nameof(Target.Method)), collection);

        var provider = collection.BuildServiceProvider();

        Assert.IsType<Logging.XunitLoggerProvider>(Assert.Single(provider.GetServices<ILoggerProvider>()));
    }

    /// <summary>Installing again never replaces what is there.</summary>
    [Fact]
    public void InstallIsIdempotent() {
        var installed = CurrentTest.Provider;

        XunitCurrentTestProvider.Install();

        Assert.Same(installed, CurrentTest.Provider);
    }

    private class Target {
        public void Method() { }
    }

    private sealed class FakeProvider : ICurrentTestProvider {
        public object? Key { get; } = new();

        public Assembly? Assembly { get; } = typeof(string).Assembly;

        public string? DisplayName => "a test";

        public ILoggerProvider CreateLoggerProvider() => throw new NotSupportedException();
    }
}
