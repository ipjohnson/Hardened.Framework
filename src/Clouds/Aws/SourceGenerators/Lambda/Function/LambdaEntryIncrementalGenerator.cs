using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

internal class LambdaEntryIncrementalGenerator {
    public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> applicationValuesProvider) {
        initializationContext.RegisterSourceOutput(
            applicationValuesProvider,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(GenerateLambdaPackage)
        );
    }

    private static void GenerateLambdaPackage(SourceProductionContext context,
        EntryPointSelector.Model model) {
        var lambdaHandlerPackageFileWriter = new LambdaHandlerPackageFileWriter();
        var csharpFile = new CSharpFileDefinition(model.EntryPointType.Namespace);

        lambdaHandlerPackageFileWriter.WriteFile(context, model, csharpFile);

        var output = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(output);

        context.AddSource(model.EntryPointType.Name + ".LambdaHandlerPackage.cs", output.Output());
    }
}
