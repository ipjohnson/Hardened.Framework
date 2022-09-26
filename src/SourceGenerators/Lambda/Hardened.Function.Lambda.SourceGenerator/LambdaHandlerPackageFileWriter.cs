using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaHandlerPackageFileWriter
    {
        public void WriteFile(SourceProductionContext context, 
            EntryPointSelector.Model appModel, ImmutableArray<RequestHandlerModel> requestHandlers, CSharpFileDefinition csharpFile)
        {
            var appClass = csharpFile.AddClass(appModel.EntryPointType.Name);

            appClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            CreateHandlerPackageClass(appModel, requestHandlers, appClass);

            SetupDiForPackageClass(appModel, requestHandlers, appClass);
        }

        private void SetupDiForPackageClass(EntryPointSelector.Model appModel,
            ImmutableArray<RequestHandlerModel> requestHandlerModels, ClassDefinition appClass)
        {
            var templateField = appClass.AddField(typeof(int), "_lambdaPackageDi");

            templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
            templateField.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeDependencyInjection);
            templateField.InitializeValue = $"DependencyRegistry<{appClass.Name}>.Register(LambdaPackageDi)";

            var diMethod = appClass.AddMethod("LambdaPackageDi");

            diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

            var environment = diMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
            var entryPoint = diMethod.AddParameter(appModel.EntryPointType, "entryPoint");

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Lambda.ILambdaHandlerPackage, TypeDefinition.Get("", "LambdaHandlerPackage") }));

            var handlerTypes = requestHandlerModels.Select(m => m.ControllerType).Distinct();

            foreach (var handlerType in handlerTypes)
            {
                diMethod.AddIndentedStatement(
                    serviceCollection.InvokeGeneric("AddTransient", new[] { handlerType }));
            }
        }

        private void CreateHandlerPackageClass(EntryPointSelector.Model appModel, ImmutableArray<RequestHandlerModel> requestHandlers, ClassDefinition appClass)
        {
            var lambdaHandlerPackage = appClass.AddClass("LambdaHandlerPackage");

            lambdaHandlerPackage.Modifiers = ComponentModifier.Private;
            lambdaHandlerPackage.AddBaseType(KnownTypes.Lambda.ILambdaHandlerPackage);

            var method = lambdaHandlerPackage.AddMethod("GetFunctionHandler");
            method.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

            var serviceProvider = method.AddParameter(KnownTypes.DI.IServiceProvider, "serviceProvider");
            var context = method.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");
            var functionName = method.Assign(context.Property("FunctionName")).ToVar("functionName");

            foreach (var requestHandlerModel in requestHandlers)
            {
                var quoteString = QuoteString(requestHandlerModel.Name.Path);
                var functionNameInstance = new InstanceDefinition(quoteString);
                
                var ifStatement = 
                    method.If(functionNameInstance.Invoke("Equals", functionName, "StringComparison.CurrentCultureIgnoreCase"));

                ifStatement.Return(New(requestHandlerModel.InvokeHandlerType, serviceProvider));
            }

            method.Return(Null());
        }

        
    }
}
