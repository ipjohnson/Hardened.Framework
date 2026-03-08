using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Library.SourceGenerator;

[Generator]
public class LibrarySourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        var generator = new ServiceProviderFileGenerator();

        context.RegisterSourceOutput(
            applicationModel,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(generator.GenerateFile));

        ConfigurationIncrementalGenerator.Setup(context, applicationModel);
    }
}
