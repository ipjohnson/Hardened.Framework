using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Templates;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Templates;

/// <summary>
/// A feature marker that is also a DependencyModules module.
/// </summary>
/// <remarks>
/// <para>
/// A package shipping both services and a generated type is then one attribute rather than two.
/// <c>[Enable&lt;T&gt;]</c> constrains <c>T</c> to <c>new()</c>, which is what a module needs
/// anyway, so allowing it costs nothing.
/// </para>
/// <para>
/// <c>AddModule</c> is DependencyModules' own entry point and goes through the module's
/// <c>PopulateServiceCollection</c>, which composes its nested modules, decorators and features
/// exactly as applying its attribute would. What differs is position: these arrive with the other
/// generated registrations rather than where the attribute was written.
/// </para>
/// </remarks>
public class EnableModuleTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),
        typeof(FromBodyAttribute),
        typeof(TemplateBaseAttribute)
    ];

    /// <summary>
    /// A marker implementing <c>IDependencyModule</c> outright, which is the shape a marker from a
    /// referenced package has: its interface is in metadata, so the semantic model sees it.
    /// </summary>
    private const string ModuleMarker =
        """
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace Other.Engine;

        public sealed class EngineModule : IDependencyModule {
            public void PopulateServiceCollection(IServiceCollection services) { }
        }

        public sealed class PlainMarker { }
        """;

    private static GeneratorResult Generate(string markers) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Engine.cs"] = ModuleMarker,
                ["Application.cs"] = $$"""
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Other.Engine;

                    namespace TestApp;

                    [HardenedModule]
                    {{markers}}
                    public partial class Application { }

                    public class HomeController {
                        [Get("/")]
                        public string Home() => "home";
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    [Fact]
    public void AMarkerThatIsAModuleIsRegistered() {
        var routing = Generate("[Enable<EngineModule>]").AssertNoErrors().SourceContaining("Routing");

        Assert.Contains("AddModule(serviceCollection, new global::Other.Engine.EngineModule())", routing);
    }

    /// <summary>
    /// A marker that is not a module registers nothing. Most are not - a marker exists to name a
    /// feature, and carrying services is the exception.
    /// </summary>
    [Fact]
    public void AMarkerThatIsNotAModuleRegistersNothing() {
        var routing = Generate("[Enable<PlainMarker>]").AssertNoErrors().SourceContaining("Routing");

        Assert.DoesNotContain("AddModule", routing);
    }

    /// <summary>And a module with no feature attributes still registers, since that is the point.</summary>
    [Fact]
    public void ItDoesNotDependOnTheMarkerDeclaringAnyOtherFacet() {
        var result = Generate("[Enable<EngineModule>]").AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Engine"));
        Assert.Contains("AddModule", result.SourceContaining("Routing"));
    }
}
