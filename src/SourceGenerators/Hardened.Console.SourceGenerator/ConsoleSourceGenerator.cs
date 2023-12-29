using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Console.SourceGenerator;

[Generator]
public class ConsoleSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        ApplicationMainGenerator.Setup(context, applicationModel);
        CommandDefinitionGenerator.Setup(context, applicationModel);

        //ModuleCodeGenerator.Setup(context, applicationModel);
    }
}