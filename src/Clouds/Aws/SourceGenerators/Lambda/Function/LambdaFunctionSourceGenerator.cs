using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

/// <summary>
/// One generator for every function entry point. Until 2026-09-04 a second generator claimed entry
/// points carrying <c>[StreamingLambdaFunctionModule]</c> and emitted a hand-rolled host for the
/// Lambda Runtime API. There is one host now, on <c>Amazon.Lambda.RuntimeSupport</c>, and whether
/// a response streams is a deployment setting, so there is nothing left to select between.
/// </summary>
[Generator]
public class LambdaFunctionSourceGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
        LambdaApplicationGenerator.Setup(context, applicationModel);
    }
}
