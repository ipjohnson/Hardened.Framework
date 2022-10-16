using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

internal class LambdaEntryIncrementalGenerator
{
    public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> applicationValuesProvider)
    {
        var methodSelector = new SyntaxSelector<MethodDeclarationSyntax>(KnownTypes.Requests.HardenedFunctionAttribute);

        var modelProvider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
            methodSelector.Where,
            LambdaFunctionModelGenerator.GenerateRequestModel
        ).WithComparer(new RequestHandlerModelComparer());

        var applicationCollect = applicationValuesProvider.Collect();

        var invokeGenerator = new LambdaFunctionInvokerFileWriter();
            
        initializationContext.RegisterSourceOutput(
            modelProvider.Combine(applicationCollect),
            SourceGeneratorWrapper.Wrap <
                (RequestHandlerModel entryModel, ImmutableArray<EntryPointSelector.Model> appModel) >( invokeGenerator.GenerateSource)
        );

        var lambdaCollection = modelProvider.Collect();

        initializationContext.RegisterSourceOutput(
            applicationValuesProvider.Combine(lambdaCollection),
            SourceGeneratorWrapper.Wrap<
                (EntryPointSelector.Model, ImmutableArray<RequestHandlerModel>)>(GenerateLambdaPackage)
        );
    }

    private static void GenerateLambdaPackage(SourceProductionContext context, (EntryPointSelector.Model,ImmutableArray<RequestHandlerModel>) model)
    {
        var lambdaHandlerPackageFileWriter = new LambdaHandlerPackageFileWriter();
        var csharpFile = new CSharpFileDefinition(model.Item1.EntryPointType.Namespace);

        lambdaHandlerPackageFileWriter.WriteFile(context, model.Item1, model.Item2, csharpFile);
            
        var output = new OutputContext();
            
        csharpFile.WriteOutput(output);

        context.AddSource(model.Item1.EntryPointType.Name + ".LambdaHandlerPackage.cs", output.Output());
    }
}