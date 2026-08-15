using Xunit;

namespace Hardened.Console.SourceGenerator.Tests;

/// <summary>
/// <c>[Enable&lt;T&gt;]</c> on a console entry point.
///
/// <para>
/// The console generator emits nothing for a feature marker — it has no template bases or link
/// types to scope. It reads them anyway, because <c>EntryPointSelector</c> is shared source and
/// populates <c>EnabledFeatures</c> for every generator that compiles it in. That makes this a
/// tolerance contract rather than an output contract: a console application that references a
/// feature package and enables it must still generate its commands, whichever way the attribute is
/// spelled and whatever the marker turns out to be.
/// </para>
///
/// <para>
/// It matters because the model's equality includes <c>EnabledFeatures</c>, so the selector runs on
/// every keystroke in the IDE. A marker shape it mishandles is a generator that throws, or one that
/// compares unequal every time and re-emits the whole file on each edit.
/// </para>
/// </summary>
public class EnabledFeatureTests {

    /// <summary>
    /// Feature markers as a referenced package ships them. <c>ModuleMarker</c> implements
    /// <c>IDependencyModule</c> outright, which is the shape whose interface is in metadata;
    /// <c>AttributedModuleMarker</c> carries <c>[DependencyModule]</c> instead, which is the shape
    /// declared in this compilation whose interface only DependencyModules' own generator would add.
    /// </summary>
    private const string Markers =
        """
        using System;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace Other.Engine;

        public sealed class ModuleMarker : IDependencyModule {
            public void PopulateServiceCollection(IServiceCollection services) { }
        }

        [AttributeUsage(AttributeTargets.Class)]
        public class DependencyModuleAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Class)]
        public class EngineNameAttribute : Attribute {
            public EngineNameAttribute(string name) { Name = name; }
            public string Name { get; }
        }

        [AttributeUsage(AttributeTargets.Class)]
        public class EngineBaseAttribute : Attribute {
            public EngineBaseAttribute(Type baseType) { BaseType = baseType; }
            public Type BaseType { get; }
        }

        [AttributeUsage(AttributeTargets.Class)]
        public class EngineFlagAttribute : Attribute { }

        [DependencyModule]
        public sealed class AttributedModuleMarker { }

        public sealed class PlainMarker { }

        [EngineName("fluid")]
        [EngineBase(typeof(EngineTemplate<>))]
        [EngineFlag]
        public sealed class FacetedMarker { }

        public class EngineTemplate<TModel> { }
        """;

    private const string GreetCommand =
        """
        [Command("greet", Description = "Say hello")]
        public class GreetCommand {
            public string Name { get; set; } = "";
        }
        """;

    /// <summary>
    /// Compiles a console application whose entry point carries <paramref name="enables"/>, and
    /// returns the emitted entry point.
    /// </summary>
    private static string Generate(string enables) {
        var application =
            $$"""
              using Hardened.Commands;
              using Hardened.Commands.Attributes;
              using Hardened.Shared.Runtime.Attributes;
              using Other.Engine;
              using System.Threading.Tasks;

              namespace TestApp;

              [HardenedModule]
              {{enables}}
              public partial class Application { }

              {{GreetCommand}}
              """;

        return ConsoleGeneratorTest.Generate(
                new Dictionary<string, string> {
                    ["Engine.cs"] = Markers,
                    ["Commands.cs"] = application
                })
            .AssertNoErrors()
            .SourceContaining("Application.Commands");
    }

    /// <summary>
    /// The three spellings are one attribute. A generator recognising only the first would silently
    /// do nothing for a project that wrote either of the others — no diagnostic, no output, and a
    /// feature that simply never turns on.
    /// </summary>
    [Theory]
    [InlineData("[Enable<PlainMarker>]")]
    [InlineData("[EnableAttribute<PlainMarker>]")]
    [InlineData("[Hardened.Shared.Runtime.Attributes.Enable<PlainMarker>]")]
    public void EverySpellingOfEnableIsAccepted(string enables) {
        Assert.Contains("CommandDefinitionProvider", Generate(enables));
    }

    /// <summary>
    /// Both ways of being a DependencyModules module are recognised: the interface, which a
    /// referenced package carries in metadata, and the attribute, which is all that is visible for a
    /// marker declared in the compilation being generated.
    /// </summary>
    [Theory]
    [InlineData("[Enable<ModuleMarker>]")]
    [InlineData("[Enable<AttributedModuleMarker>]")]
    [InlineData("[Enable<PlainMarker>]")]
    public void AMarkerIsReadWhicheverWayItDeclaresItself(string enables) {
        Assert.Contains("CommandDefinitionProvider", Generate(enables));
    }

    /// <summary>
    /// A marker's own attributes become facets, and the three argument shapes all survive: a string
    /// argument, a <c>typeof</c> argument reduced to a type definition, and no argument at all.
    /// </summary>
    [Fact]
    public void AMarkerCarryingEveryFacetShapeIsRead() {
        Assert.Contains("CommandDefinitionProvider", Generate("[Enable<FacetedMarker>]"));
    }

    /// <summary>
    /// Several features on one entry point are all read rather than only the first.
    /// </summary>
    [Fact]
    public void MoreThanOneFeatureCanBeEnabled() {
        Assert.Contains(
            "CommandDefinitionProvider",
            Generate("[Enable<PlainMarker>]\n[Enable<ModuleMarker>]\n[Enable<FacetedMarker>]"));
    }

    /// <summary>
    /// Attributes that are not <c>[Enable&lt;T&gt;]</c> are passed over — a generic attribute with
    /// another name, and one with the wrong number of type arguments, both of which reach the same
    /// code that has to decline them.
    /// </summary>
    [Fact]
    public void AnAttributeThatIsNotEnableIsPassedOver() {
        Assert.Contains("CommandDefinitionProvider", Generate("[System.Obsolete]"));
    }

    /// <summary>An entry point enabling nothing is the ordinary case and stays ordinary.</summary>
    [Fact]
    public void AnEntryPointWithNoFeaturesGeneratesAsBefore() {
        Assert.Contains("CommandDefinitionProvider", Generate(""));
    }
}
