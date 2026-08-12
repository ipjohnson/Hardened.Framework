using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Runs <see cref="EntryPointSelector"/>'s syntax provider and keeps the models it produced.
///
/// <para>
/// The transform is the only place a <c>[HardenedModule]</c> class becomes something the rest of the
/// generators can read: its type, its attributes, its methods and its settable properties. Every
/// shipped generator consumes that model and none of them expose it, so the way to assert on the
/// model itself is to run the same provider and keep what comes out.
/// </para>
///
/// <para>
/// A <see cref="GeneratorSyntaxContext"/> cannot be constructed by a test — the transform only ever
/// runs inside a driver — so this is also the only route to the semantic-model code the transform
/// reaches through: <c>MethodDefinitionSyntaxExtensions</c> and <c>TypeSyntaxExtensions</c>.
/// </para>
/// </summary>
public class EntryPointCaptureGenerator(bool rootEntryPoint = false) : IIncrementalGenerator {

    private readonly List<EntryPointSelector.Model> _models = [];

    /// <summary>Every model the transform produced, in the order the driver produced them.</summary>
    public IReadOnlyList<EntryPointSelector.Model> Models => _models;

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var provider = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(rootEntryPoint));

        context.RegisterSourceOutput(provider, (production, model) => {
            _models.Add(model);

            // The file exists so the run has an output to attribute a crash to. The assertions are
            // on the captured model, but a run that emits nothing cannot be told apart from a run
            // whose transform threw - see GeneratorCrashHandlingTests.
            production.AddSource(
                model.EntryPointType.Name + ".Captured.cs",
                "// " + model.EntryPointType.Namespace + "." + model.EntryPointType.Name);
        });
    }
}

/// <summary>Runs <see cref="EntryPointCaptureGenerator"/> over one source file.</summary>
public static class EntryPointCapture {

    /// <summary>
    /// A <c>[HardenedModule]</c> entry point built around <paramref name="members"/>, plus whatever
    /// supporting declarations a case needs after it.
    /// </summary>
    public static string Application(string members = "", string trailing = "", string attributes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        {{attributes}}
        public partial class Application {
        {{members}}
        }

        {{trailing}}
        """;

    /// <summary>
    /// The single model the transform produced for <paramref name="source"/>.
    /// </summary>
    /// <remarks>
    /// The generated file is asserted on as well as the compilation. A transform that threw leaves
    /// the driver with no output and <c>AssertNoErrors</c> still passes — the same blind spot
    /// <c>SourceGeneratorWrapper</c> opens one stage later.
    /// </remarks>
    public static EntryPointSelector.Model Single(string source, bool rootEntryPoint = false) {
        var generator = new EntryPointCaptureGenerator(rootEntryPoint);

        var result = GeneratorTestHarness.Run(
            source, generator, RequestGeneratorHarness.Anchors);

        result.AssertNoErrors();

        Assert.Contains("Application.Captured.cs", result.GeneratedSources.Keys);

        return Assert.Single(generator.Models);
    }
}
