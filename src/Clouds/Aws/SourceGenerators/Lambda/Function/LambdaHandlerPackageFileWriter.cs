using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

public class LambdaHandlerPackageFileWriter {
    public void WriteFile(SourceProductionContext context,
        EntryPointSelector.Model appModel, ImmutableArray<RequestHandlerModel> requestHandlers,
        CSharpFileDefinition csharpFile) {
        var appClass = csharpFile.AddClass(appModel.EntryPointType.Name);

        appClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        CreateHandlerPackageClass(appModel, requestHandlers, appClass);

        SetupDiForPackageClass(appModel, requestHandlers, appClass);
    }

    private void SetupDiForPackageClass(EntryPointSelector.Model appModel,
        ImmutableArray<RequestHandlerModel> requestHandlerModels, ClassDefinition appClass) {
        var templateField = appClass.AddField(typeof(int), "_lambdaPackageDi");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace("DependencyModules.Runtime.Helpers");
        templateField.InitializeValue = new CodeOutputComponent($"DependencyRegistry<{appClass.Name}>.Add(LambdaPackageDi)");
        templateField.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"), "nameof(LambdaPackageDi)");

        var diMethod = appClass.AddMethod("LambdaPackageDi");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Lambda.ILambdaHandlerPackage, TypeDefinition.Get("", "LambdaHandlerPackage") }));

        var handlerTypes = requestHandlerModels.Select(m => m.ControllerType).Distinct();

        foreach (var handlerType in handlerTypes) {
            diMethod.AddIndentedStatement(
                serviceCollection.InvokeGeneric("AddTransient", new[] { handlerType }));
        }
    }

    private void CreateHandlerPackageClass(EntryPointSelector.Model appModel,
        ImmutableArray<RequestHandlerModel> requestHandlers, ClassDefinition appClass) {
        var lambdaHandlerPackage = appClass.AddClass("LambdaHandlerPackage");

        lambdaHandlerPackage.Modifiers = ComponentModifier.Private;
        lambdaHandlerPackage.AddBaseType(KnownTypes.Lambda.ILambdaHandlerPackage);

        var method = lambdaHandlerPackage.AddMethod("GetFunctionHandler");
        method.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

        var serviceProvider = method.AddParameter(KnownTypes.DI.IServiceProvider, "serviceProvider");
        var lambdaContext = method.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");

        if (requestHandlers.Length == 1) {
            method.Return(New(requestHandlers.Single().InvokeHandlerType, serviceProvider));
        } else {
            var switchBlock = method.Switch("context.FunctionName");

            foreach (var handler in requestHandlers) {
                var caseBlock = switchBlock.AddCase($"\"{handler.Name.Path}\"");
                caseBlock.Return(New(handler.InvokeHandlerType, serviceProvider));
            }

            method.Return(new CodeOutputComponent("null"));
        }
    }
}