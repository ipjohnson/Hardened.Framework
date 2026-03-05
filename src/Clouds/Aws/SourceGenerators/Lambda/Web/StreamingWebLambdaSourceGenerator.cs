using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

[Generator]
public class StreamingWebLambdaSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, _) => node is ClassDeclarationSyntax &&
                         node.IsAttributed("StreamingLambdaWebApplication"),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        StreamingBootstrapGenerator.Setup(context, applicationModel);
    }
}
