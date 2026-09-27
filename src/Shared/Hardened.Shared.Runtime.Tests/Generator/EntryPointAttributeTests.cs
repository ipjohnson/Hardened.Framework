using Hardened.DependencyModules.SourceGenerator;
using Hardened.Library.SourceGenerator;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.SourceGeneration.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Generator;

/// <summary>
/// A module's own attributes, as the library generator's entry point transform reads them.
/// </summary>
/// <remarks>
/// Every module in an application passes through that transform, and the library generator compiles
/// its own copy of it, so these are run through that generator rather than the web one.
/// </remarks>
public class EntryPointAttributeTests
{
    private static readonly Type[] Anchors =
    [
        typeof(HardenedModuleAttribute), // Hardened.Shared.Runtime
        typeof(IConfigurationPackage), // Hardened.Shared.Runtime
        typeof(IServiceCollection), // Microsoft.Extensions.DependencyInjection.Abstractions
    ];

    /// <summary>
    /// A constant string, a constant of another type, and a value that is not a constant. The
    /// transform keeps the constant strings for a diagnostic to read, and the other two pass
    /// through it.
    /// </summary>
    [Fact]
    public void AModulesPositionalArgumentsOfEveryKindAreRead()
    {
        var result = GeneratorTestHarness
            .Run(
                new Dictionary<string, string>
                {
                    ["Test.cs"] = """
                    using System;
                    using Hardened.Shared.Runtime.Attributes;

                    namespace TestApp;

                    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                    public sealed class NoteAttribute(object value) : Attribute;

                    [HardenedModule]
                    [Note("text")]
                    [Note(42)]
                    [Note(typeof(int))]
                    public partial class TestModule { }
                    """,
                },
                [new LibrarySourceGenerator(), new HardenedSourceGenerator()],
                Anchors
            )
            .AssertNoErrors();

        Assert.Empty(result.GeneratorExceptions);
        Assert.Contains(result.GeneratedSources.Keys, key => key.Contains("TestModule"));
    }
}
