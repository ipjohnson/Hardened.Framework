using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

[Generator]
public class WebLambdaSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) &&
                         !node.IsAttributed("StreamingLambdaWebApplication"),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel);
    }
}