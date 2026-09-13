using Hardened.SourceGenerator.Function;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.SourceGenerator;

[Generator]
public class FunctionLibrarySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var applicationModel = context
            .SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(false)
            )
            .WithComparer(new EntryPointSelector.Comparer());

        FunctionIncrementalGenerator.Setup(context, applicationModel);
    }
}
