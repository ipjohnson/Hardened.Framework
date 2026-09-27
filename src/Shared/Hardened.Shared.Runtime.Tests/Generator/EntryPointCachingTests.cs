using Hardened.DependencyModules.SourceGenerator;
using Hardened.Library.SourceGenerator;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Configuration;
using Hardened.SourceGeneration.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Generator;

/// <summary>
/// The library generator's entry point model is compared by value between runs, so an edit that
/// does not touch a module leaves its generated source cached.
/// </summary>
/// <remarks>
/// The model carries the assembly's name, which titles the published document, and a comparison
/// that missed a member would rebuild every module on every keystroke.
/// </remarks>
public class EntryPointCachingTests
{
    private static readonly Type[] Anchors =
    [
        typeof(HardenedModuleAttribute), // Hardened.Shared.Runtime
        typeof(IConfigurationPackage), // Hardened.Shared.Runtime
        typeof(IServiceCollection), // Microsoft.Extensions.DependencyInjection.Abstractions
    ];

    private const string Module = """
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class TestModule { }
        """;

    [Fact]
    public void AnEditElsewhereLeavesTheModulesSourceCached()
    {
        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string>
            {
                ["Module.cs"] = Module,
                ["Other.cs"] = "namespace TestApp; public class Other { }",
            },
            new Dictionary<string, string>
            {
                ["Module.cs"] = Module,
                ["Other.cs"] = "namespace TestApp; public class Other { public int Added; }",
            },
            [new LibrarySourceGenerator(), new HardenedSourceGenerator()],
            Anchors
        );

        Assert.True(
            result.AllOutputsCached,
            "Recomputed: " + string.Join(", ", result.OutputReasons)
        );
        Assert.Equal(result.FirstRun, result.SecondRun);
    }
}
