using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaEntryPointFileWriter
    {
        public void GenerateSource(
            SourceProductionContext sourceContext, 
            (LambdaFunctionEntryModel entryModel, ImmutableArray<ApplicationSelector.Model> appModel) data)
        {
            var entryModel = data.entryModel;
            var appModel = data.appModel.First();

            var generatedFile = GenerateFile(entryModel, appModel);

            File.AppendAllText(@"C:\temp\generated\"+ entryModel.Handler.Name + ".FunctionHandler.cs", generatedFile);
            
            sourceContext.AddSource(entryModel.Handler.Name + ".FunctionHandler.cs", generatedFile);
        }

        private string GenerateFile(LambdaFunctionEntryModel entryModel, ApplicationSelector.Model appModel)
        {
            var csharpFile = new CSharpFileDefinition(entryModel.Handler.Namespace);

            GenerateEntryPointClass(csharpFile, entryModel, appModel);

            var outputContext=  new OutputContext();

            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }

        private void GenerateEntryPointClass(CSharpFileDefinition csharpFile,
            LambdaFunctionEntryModel lambdaFunctionEntryModel,
            ApplicationSelector.Model appModel)
        {
            var lambdaClass = csharpFile.AddClass(lambdaFunctionEntryModel.Handler.Name + "_" + lambdaFunctionEntryModel.Name);

            lambdaClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            AssignBaseType(lambdaClass, lambdaFunctionEntryModel);

            GenerateClassImpl(lambdaClass, lambdaFunctionEntryModel, appModel);

            GenerateInvokeClass(lambdaClass, lambdaFunctionEntryModel, appModel);
        }

        private void GenerateInvokeClass(ClassDefinition lambdaClass, LambdaFunctionEntryModel lambdaFunctionEntryModel, ApplicationSelector.Model appModel)
        {
            LambdaFunctionInvokeWriter.GenerateInvokeClass(lambdaClass, lambdaFunctionEntryModel, appModel);
        }

        private void GenerateClassImpl(ClassDefinition lambdaClass, LambdaFunctionEntryModel lambdaFunctionEntryModel, ApplicationSelector.Model appModel)
        {
            var lambdaFunctionImplField = lambdaClass.AddField(KnownTypes.Lambda.ILambdaFunctionImplService,
                "_lambdaFunctionImplService");
            var applicationField = lambdaClass.AddField(appModel.ApplicationType, "_application");

            GenerateConstructors(lambdaClass, lambdaFunctionEntryModel, appModel, lambdaFunctionImplField,
                applicationField);

            GenerateInvokeMethod(lambdaClass, lambdaFunctionEntryModel, applicationField, lambdaFunctionImplField);

            GenerateProviderProperty(lambdaClass, lambdaFunctionEntryModel, applicationField);
        }

        private void GenerateProviderProperty(ClassDefinition lambdaClass, LambdaFunctionEntryModel lambdaFunctionEntryModel, FieldDefinition applicationField)
        {
            var property = lambdaClass.AddProperty(KnownTypes.DI.IServiceProvider, "Provider");

            property.Get.LambdaSyntax = true;
            property.Get.AddIndentedStatement(applicationField.Instance.Property("Provider"));
            property.Set = null;
        }

        private void GenerateInvokeMethod(ClassDefinition lambdaClass,
            LambdaFunctionEntryModel lambdaFunctionEntryModel,
            FieldDefinition applicationField,
            FieldDefinition lambdaFunctionImplField)
        {
            var invokeMethod = lambdaClass.AddMethod("Invoke");
            invokeMethod.SetReturnType(TypeDefinition.Task(typeof(Stream)));

            var inputStream = invokeMethod.AddParameter(typeof(Stream), "inputStream");
            var lambdaContext = invokeMethod.AddParameter(KnownTypes.Lambda.ILambdaContext, "lambdaContext");

            invokeMethod.Return(lambdaFunctionImplField.Instance.Invoke("InvokeFunction", inputStream, lambdaContext));
        }

        private void GenerateConstructors(ClassDefinition lambdaClass, LambdaFunctionEntryModel lambdaFunctionEntryModel, ApplicationSelector.Model appModel, FieldDefinition lambdaFunctionImplField, FieldDefinition applicationField)
        {
            lambdaClass.AddConstructor(This(New(KnownTypes.Application.EnvironmentImpl), Null()));

            var constructor = lambdaClass.AddConstructor();

            var envParam = constructor.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var overrides =
                constructor.AddParameter(TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            constructor.Assign(New(appModel.ApplicationType, envParam, overrides)).To(applicationField.Instance);
            var filterVariable = constructor.Assign(New(KnownTypes.Lambda.LambdaInvokeFilter, "new InvokeFilter(_application.Provider)"))
                .ToVar("filter");

            constructor.AddUsingNamespace(KnownTypes.Namespace.HardenedRequestsAbstractMiddleware);
            constructor.AddIndentedStatement(
                "_application.Provider.GetService<IMiddlewareService>()!.Use(_ => filter)");
            constructor.AddIndentedStatement("_lambdaFunctionImplService = _application.Provider.GetRequiredService<ILambdaFunctionImplService>()");
        }

        private void AssignBaseType(ClassDefinition lambdaClass, LambdaFunctionEntryModel lambdaFunctionEntryModel)
        {
            var interfaceType =
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition, 
                    KnownTypes.Lambda.ILambdaHandler.Name, 
                    KnownTypes.Lambda.ILambdaHandler.Namespace,
                    new[] { lambdaFunctionEntryModel.ResponseInformation.ReturnType! });

            File.AppendAllText(@"C:\temp\generated\response.txt", lambdaFunctionEntryModel.ResponseInformation.ReturnType!.Namespace + "\r\n");
            lambdaClass.AddUsingNamespace(lambdaFunctionEntryModel.ResponseInformation.ReturnType!.Namespace);
            lambdaClass.AddBaseType(interfaceType);
        }
    }
}
