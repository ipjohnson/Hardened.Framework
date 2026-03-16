using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

[Generator]
public class LambdaFunctionSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) &&
                         !node.IsAttributed("StreamingLambdaFunction") &&
                         !node.IsAttributed("StreamingLambdaFunctionApplication") &&
                         !node.IsAttributed("LambdaFunctionApplication"),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
        LambdaApplicationGenerator.Setup(context, applicationModel);
    }
}