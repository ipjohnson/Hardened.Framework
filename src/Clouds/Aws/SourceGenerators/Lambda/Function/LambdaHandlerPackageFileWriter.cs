using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

public class LambdaHandlerPackageFileWriter {
    private static readonly ITypeDefinition IFunctionHandlerProvider =
        TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition,
            KnownTypes.Namespace.Hardened.Requests.Abstract.Execution, "IFunctionHandlerProvider");
    public void WriteFile(SourceProductionContext context,
        EntryPointSelector.Model appModel,
        CSharpFileDefinition csharpFile) {
        var appClass = csharpFile.AddClass(appModel.EntryPointType.Name);

        appClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        CreateHandlerPackageClass(appClass);

        SetupDiForPackageClass(appClass, appModel.EntryPointType.Namespace);
    }

    private void SetupDiForPackageClass(ClassDefinition appClass, string appNamespace) {
        var templateField = appClass.AddField(typeof(int), "_lambdaPackageDi");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace("DependencyModules.Runtime.Helpers");
        templateField.InitializeValue =
            new CodeOutputComponent($"DependencyRegistry<{appClass.Name}>.Add(LambdaPackageDi)");
        templateField.AddAttribute(
            TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"),
            "nameof(LambdaPackageDi)");

        var diMethod = appClass.AddMethod("LambdaPackageDi");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        // AddSingleton is an extension method, and an extension method is reachable only through a
        // using of its namespace - global:: cannot name one.
        diMethod.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] {
                KnownTypes.Lambda.ILambdaHandlerPackage,
                // The nested package class by its full name: an empty-namespace TypeDefinition
                // means the global namespace from CSharpAuthor 2.0 on, which Global mode
                // qualifies - and global::LambdaHandlerPackage names nothing.
                TypeDefinition.Get(appNamespace, appClass.Name + ".LambdaHandlerPackage")
            }));
    }

    private void CreateHandlerPackageClass(ClassDefinition appClass) {
        var lambdaHandlerPackage = appClass.AddClass("LambdaHandlerPackage");

        lambdaHandlerPackage.Modifiers = ComponentModifier.Private;
        lambdaHandlerPackage.AddBaseType(KnownTypes.Lambda.ILambdaHandlerPackage);

        // Field for IFunctionHandlerProvider collection
        var providersType = new GenericTypeDefinition(typeof(IEnumerable<>),
            new[] { IFunctionHandlerProvider });

        var field = lambdaHandlerPackage.AddField(providersType, "_providers");

        // Constructor
        var constructor = lambdaHandlerPackage.AddConstructor();
        var providersParam = constructor.AddParameter(providersType, "providers");
        constructor.Assign(providersParam).To(field.Instance);

        // GetFunctionHandler method
        var method = lambdaHandlerPackage.AddMethod("GetFunctionHandler");
        method.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

        var serviceProvider = method.AddParameter(KnownTypes.DI.IServiceProvider, "serviceProvider");
        var lambdaContext = method.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");

        var foreachBlock = method.ForEach("provider", new CodeOutputComponent("_providers"));

        foreachBlock.Assign(
            new CodeOutputComponent("provider.GetFunctionHandler(context.FunctionName, serviceProvider)")
        ).ToVar("handler");

        var ifBlock = foreachBlock.If("handler != null");
        ifBlock.Return(new CodeOutputComponent("handler"));

        method.Return(new CodeOutputComponent("null"));
    }
}
