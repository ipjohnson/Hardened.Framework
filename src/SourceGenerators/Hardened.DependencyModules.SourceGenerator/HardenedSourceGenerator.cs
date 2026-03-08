using System.Collections.Immutable;
using CSharpAuthor;
using DependencyModules.SourceGenerator;
using DependencyModules.SourceGenerator.Impl;
using DependencyModules.SourceGenerator.Impl.Models;
using Microsoft.CodeAnalysis;

namespace Hardened.DependencyModules.SourceGenerator;

[Generator]
public class HardenedSourceGenerator : BaseSourceGenerator {

    private static readonly ITypeDefinition HardenedModuleAttribute =
        TypeDefinition.Get(TypeDefinitionEnum.ClassDefinition,
            "Hardened.Shared.Runtime.Attributes", "HardenedModuleAttribute");

    protected override bool ShouldAutoApproveCompilationUnit => false;

    protected override ITypeDefinition[] ModuleAttributeTypes() {
        return new[] { HardenedModuleAttribute };
    }

    protected override IEnumerable<IDependencyModuleSourceGenerator> AttributeSourceGenerators() {
        yield return new ServiceSourceGenerator();
    }

    protected override void SetupRootGenerator(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<(ModuleEntryPointModel Left, DependencyModuleConfigurationModel Right)>> valuesProvider) {

        var moduleWriter = new DependencyModuleWriter(true);

        context.RegisterSourceOutput(
            valuesProvider,
            moduleWriter.GenerateSource
        );
    }
}
