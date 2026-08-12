using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Templates.Abstract;
using Hardened.Templates.Runtime.Impl;

namespace Hardened.Templates.SourceGenerator.Tests;

/// <summary>
/// Drives <see cref="TemplateSourceGenerator"/> the way a real project does.
///
/// <para>
/// Templates reach the generator as <c>AdditionalFiles</c>, never as compilation sources, so every
/// case here supplies them through the harness's <c>additionalTexts</c> argument. A consuming
/// project gets the same effect from the <c>None Remove</c> + <c>AdditionalFiles</c> pairing in its
/// csproj; a template that is only a <c>Content</c> item is invisible to the generator.
/// </para>
///
/// <para>
/// The generator also needs a <c>[HardenedModule]</c> class in the compilation. It is the anchor
/// every emitted registration hangs off, and without one nothing at all is produced.
/// </para>
/// </summary>
internal static class TemplateGeneration {

    /// <summary>
    /// One type from each assembly the generated template classes bind against. Generated code
    /// names <c>TemplateExecutionContext</c> and <c>TemplateExecutionService</c> from
    /// <c>Hardened.Templates.Runtime</c> directly, so anchoring on the abstractions alone is not
    /// enough — the emitted file would not compile and the failure would read as a generator defect.
    /// </summary>
    internal static readonly Type[] Anchors = [
        typeof(HardenedModuleAttribute),      // Hardened.Shared.Runtime
        typeof(SafeString),                   // Hardened.Templates.Abstract
        typeof(TemplateExecutionService)      // Hardened.Templates.Runtime
    ];

    /// <summary>A minimal application: the module anchor plus a handful of model shapes.</summary>
    internal const string Application = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Templates.Abstract;

        namespace TestApp;

        [HardenedModule]
        public partial class TestApplication { }

        public class Tag {
            public string Label { get; set; } = "";
            public IEnumerable<string> Aliases { get; set; } = new List<string>();
        }

        public class Person {
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public bool Active { get; set; }
            public DateTime Born { get; set; }
            public decimal Balance { get; set; }
            public string? Nickname { get; set; }
            public IEnumerable<string> Words { get; set; } = new List<string>();
            public IEnumerable<Tag> Tags { get; set; } = new List<Tag>();
        }
        """;

    /// <summary>Runs the generator over <see cref="Application"/> plus the given template files.</summary>
    internal static GeneratorResult Generate(params (string Name, string Body)[] templates) =>
        Generate(Application, templates);

    /// <summary>Runs the generator over caller-supplied C# plus the given template files.</summary>
    internal static GeneratorResult Generate(string source, params (string Name, string Body)[] templates) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["App.cs"] = source },
            [new TemplateSourceGenerator()],
            Anchors,
            templates.ToDictionary(template => template.Name, template => template.Body));

    /// <summary>The template class emitted for a template file named <paramref name="templateName"/>.</summary>
    internal static string TemplateClass(this GeneratorResult result, string templateName) =>
        result.SourceContaining("Generated.Template_" + templateName + ".cs");

    /// <summary>The partial emitted onto the application class that resolves templates by name.</summary>
    internal static string TemplateProvider(this GeneratorResult result) =>
        result.SourceContaining("TestApplication.Templates.cs");

    /// <summary>The partial emitted onto the application class that registers template helpers.</summary>
    internal static string HelperProvider(this GeneratorResult result) =>
        result.SourceContaining("TestApplication.TemplateHelpers.cs");
}
