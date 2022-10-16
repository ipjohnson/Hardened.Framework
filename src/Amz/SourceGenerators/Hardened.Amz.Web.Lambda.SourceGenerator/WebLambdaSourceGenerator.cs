using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

[Generator]
public class WebLambdaSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());
            
        WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel);
    }
}