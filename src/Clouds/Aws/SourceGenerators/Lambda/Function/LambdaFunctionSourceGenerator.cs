using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

[Generator]
public class LambdaFunctionSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();

        // The exact complement of the streaming selector, sharing its predicate so the two cannot
        // drift into overlapping or leaving a gap. Both generators run on every entry point, and an
        // application that matched both would get two bootstraps declaring the same members.
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) &&
                          !StreamingFunctionLambdaSourceGenerator.DeclaresStreaming(node),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
        LambdaApplicationGenerator.Setup(context, applicationModel);
    }
}