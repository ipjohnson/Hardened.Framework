using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

/// <summary>
/// One generator for every web entry point. Until 2026-09-04 a second generator claimed entry
/// points carrying <c>[StreamingLambdaWebModule]</c> and emitted a hand-rolled host for the Lambda
/// Runtime API; the two selectors were written as each other's negation. There is one host now,
/// on <c>Amazon.Lambda.RuntimeSupport</c>, and whether a response streams is a deployment setting
/// rather than an attribute, so there is nothing left to select between.
/// </summary>
[Generator]
public class WebLambdaSourceGenerator : IIncrementalGenerator {
    /// <summary>
    /// The attribute that marks a handler as an event stream. Fully qualified and resolved as a
    /// symbol, because the check it feeds names the handler to the operator and a name match on
    /// syntax could name the wrong one.
    /// </summary>
    private const string ServerSentEventsAttribute = "Hardened.Web.Runtime.Attributes.ServerSentEventsAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        // Which handlers stream, so the application can say at startup when its deployment cannot
        // deliver them. The build knows the handlers; only the deployment knows the mode.
        var streamingHandlers = context.SyntaxProvider.ForAttributeWithMetadataName(
            ServerSentEventsAttribute,
            static (node, _) => node is MethodDeclarationSyntax,
            static (attributeContext, _) => HandlerName(attributeContext.TargetSymbol)
        ).Collect();

        WebLambdaApplicationBootstrapGenerator.Setup(context, applicationModel.Combine(streamingHandlers));
    }

    private static string HandlerName(ISymbol handler) =>
        handler.ContainingType.ToDisplayString() + "." + handler.Name;
}
