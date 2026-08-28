using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

[Generator]
public class WebLambdaSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();

        // The exact complement of the streaming selector, sharing its predicate so the two cannot
        // drift into overlapping or leaving a gap. Both generators run on every entry point, and an
        // application that matched both would get two bootstraps declaring the same members.
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) &&
                          !StreamingWebLambdaSourceGenerator.DeclaresStreaming(node),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel);
    }
}
