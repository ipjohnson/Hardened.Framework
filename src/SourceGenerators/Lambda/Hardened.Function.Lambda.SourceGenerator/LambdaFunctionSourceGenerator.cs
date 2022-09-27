using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    [Generator]
    public class LambdaFunctionSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(true)
            );
            
            LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
            LambdaApplicationGenerator.Setup(context, applicationModel);
        }
    }
}
