using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaEntryPointFileWriter
    {
        public void GenerateSource(
            SourceProductionContext sourceContext, 
            (RequestHandlerModel entryModel, ImmutableArray<EntryPointSelector.Model> appModel) data)
        {
            sourceContext.CancellationToken.ThrowIfCancellationRequested();

            var entryModel = data.entryModel;
            var appModel = data.appModel.First();

            var generatedFile = GenerateFile(entryModel, appModel, sourceContext.CancellationToken);
            
            sourceContext.AddSource(entryModel.Name.Path + ".FunctionHandler.cs", generatedFile);
        }

        private string GenerateFile(RequestHandlerModel entryModel, EntryPointSelector.Model appModel,
            CancellationToken cancellationToken)
        {
            var csharpFile = new CSharpFileDefinition(entryModel.ControllerType.Namespace);

            GenerateEntryPointClass(csharpFile, entryModel, appModel, cancellationToken);

            var outputContext=  new OutputContext();

            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }

        private void GenerateEntryPointClass(CSharpFileDefinition csharpFile,
            RequestHandlerModel lambdaFunctionEntryModel,
            EntryPointSelector.Model appModel, 
            CancellationToken cancellationToken)
        {
            var lambdaClass = csharpFile.AddClass(lambdaFunctionEntryModel.ControllerType.Name + "_" + lambdaFunctionEntryModel.Name.Path);

            lambdaClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            AssignBaseType(lambdaClass, lambdaFunctionEntryModel);

            GenerateClassImpl(lambdaClass, lambdaFunctionEntryModel, appModel, cancellationToken);

            GenerateInvokeClass(lambdaClass, lambdaFunctionEntryModel, appModel, cancellationToken);
        }

        private void GenerateInvokeClass(ClassDefinition lambdaClass, RequestHandlerModel lambdaFunctionEntryModel,
            EntryPointSelector.Model appModel, CancellationToken cancellationToken)
        {
            InvokeClassGenerator.GenerateInvokeClass(lambdaFunctionEntryModel, lambdaClass, cancellationToken);
        }

        private void GenerateClassImpl(ClassDefinition lambdaClass, RequestHandlerModel lambdaFunctionEntryModel,
            EntryPointSelector.Model appModel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lambdaFunctionImplField = lambdaClass.AddField(KnownTypes.Lambda.ILambdaFunctionImplService,
                "_lambdaFunctionImplService");
            var provider = lambdaClass.AddField(KnownTypes.DI.IServiceProvider, "_serviceProvider");

            GenerateConstructors(lambdaClass, lambdaFunctionEntryModel, appModel, lambdaFunctionImplField);

            GenerateInvokeMethod(lambdaClass, lambdaFunctionEntryModel, lambdaFunctionImplField);

            GenerateProviderProperty(lambdaClass, lambdaFunctionEntryModel);
        }

        private void GenerateProviderProperty(ClassDefinition lambdaClass, RequestHandlerModel lambdaFunctionEntryModel)
        {
            var property = lambdaClass.AddProperty(KnownTypes.DI.IServiceProvider, "Provider");

            property.Get.LambdaSyntax = true;
            property.Get.AddIndentedStatement("_serviceProvider");
            property.Set = null;
        }

        private void GenerateInvokeMethod(ClassDefinition lambdaClass,
            RequestHandlerModel lambdaFunctionEntryModel,
            FieldDefinition lambdaFunctionImplField)
        {
            var invokeMethod = lambdaClass.AddMethod("Invoke");
            invokeMethod.SetReturnType(TypeDefinition.Task(typeof(Stream)));

            var inputStream = invokeMethod.AddParameter(typeof(Stream), "inputStream");
            var lambdaContext = invokeMethod.AddParameter(KnownTypes.Lambda.ILambdaContext, "lambdaContext");

            invokeMethod.Return(lambdaFunctionImplField.Instance.Invoke("InvokeFunction", inputStream, lambdaContext));
        }

        private void GenerateConstructors(ClassDefinition lambdaClass, RequestHandlerModel lambdaFunctionEntryModel, EntryPointSelector.Model appModel, FieldDefinition lambdaFunctionImplField)
        {
            lambdaClass.AddConstructor(This(New(KnownTypes.Application.EnvironmentImpl), Null()));

            var constructor = lambdaClass.AddConstructor();

            var envParam = constructor.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var overrides =
                constructor.AddParameter(TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            var logger = SetupLoggerFactory(appModel, constructor, envParam);
            
            var applicationField = 
                constructor.Assign(New(appModel.EntryPointType)).ToVar("applicationField");
            
            constructor.Assign(
                applicationField.Invoke("CreateServiceProvider", envParam, overrides, logger, "RegisterRequestModules")).To("_serviceProvider");


            var startupMethod = "null";

            if (appModel.MethodDefinitions.Any(m => m.Name == "Startup"))
            {
                startupMethod = "Startup";
            }

            constructor.AddIndentedStatement(
                Invoke(
                    KnownTypes.Application.ApplicationLogic,
                    "StartWithWait",
                    "_serviceProvider",
                    startupMethod,
                    15));

            var filterVariable = constructor.Assign(New(KnownTypes.Lambda.LambdaInvokeFilter, "new InvokeFilter(_serviceProvider)"))
                .ToVar("filter");

            constructor.AddUsingNamespace(KnownTypes.Namespace.HardenedRequestsAbstractMiddleware);
            constructor.AddIndentedStatement(
                "_serviceProvider.GetService<IMiddlewareService>()!.Use(_ => filter)");
            constructor.AddIndentedStatement("_lambdaFunctionImplService = _serviceProvider.GetRequiredService<ILambdaFunctionImplService>()");

            var staticRegMethod = lambdaClass.AddMethod("RegisterRequestModules");

            var environmentVar = staticRegMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollectionParam =
                staticRegMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

            staticRegMethod.Modifiers = ComponentModifier.Private | ComponentModifier.Static;
            
            staticRegMethod.AddIndentedStatement(
                Invoke(
                    KnownTypes.DI.Registry.RequestRuntimeDI,
                    "Register",
                    environmentVar,
                    serviceCollectionParam));

            staticRegMethod.AddIndentedStatement(
                Invoke(
                    KnownTypes.DI.Registry.TemplateDI,
                    "Register",
                    environmentVar,
                    serviceCollectionParam));

            staticRegMethod.AddIndentedStatement(
                Invoke(
                    KnownTypes.DI.Registry.LambdaFunctionRuntimeDI,
                    "Register",
                    environmentVar,
                    serviceCollectionParam));
        }


        private static InstanceDefinition SetupLoggerFactory(
            EntryPointSelector.Model entryPoint,
            ConstructorDefinition constructorDefinition,
            ParameterDefinition environment)
        {
            var loggingMethod = entryPoint.MethodDefinitions.FirstOrDefault(m => m.Name == "ConfigureLogging");
            var logLevelMethod = entryPoint.MethodDefinitions.FirstOrDefault(m => m.Name == "ConfigureLogLevel");

            IOutputComponent? logCreateMethod;

            if (loggingMethod != null)
            {
                logCreateMethod = CodeOutputComponent.Get("LoggerFactory.Create(builder => ConfigureLogging(environment, builder))");
            }
            else if (logLevelMethod != null)
            {
                logCreateMethod = CodeOutputComponent.Get(
                    $"LoggerFactory.Create(LambdaLoggerHelper.CreateAction(ConfigureLogLevel(environment), \"{entryPoint.EntryPointType.Namespace}\"))");
                logCreateMethod.AddUsingNamespace("Hardened.Shared.Lambda.Runtime.Logging");
            }
            else
            {
                logCreateMethod = CodeOutputComponent.Get(
                    $"LoggerFactory.Create(LambdaLoggerHelper.CreateAction(environment, \"{entryPoint.EntryPointType.Namespace}\"))");
                logCreateMethod.AddUsingNamespace("Hardened.Shared.Lambda.Runtime.Logging");
            }

            logCreateMethod.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.Logging);

            return constructorDefinition.Assign(logCreateMethod).ToVar("loggerFactory");
        }

        private void AssignBaseType(ClassDefinition lambdaClass, RequestHandlerModel lambdaFunctionEntryModel)
        {
            var bodyParameter = 
                lambdaFunctionEntryModel.RequestParameterInformationList.FirstOrDefault(
                    p => p.BindingType == ParameterBindType.Body);

            ITypeDefinition? interfaceType = null;

            if (bodyParameter != null)
            {
                interfaceType = new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    KnownTypes.Lambda.ILambdaHandler.Namespace,
                    KnownTypes.Lambda.ILambdaHandler.Name,
                    new[]
                    {
                        bodyParameter.ParameterType, 
                        lambdaFunctionEntryModel.ResponseInformation.ReturnType!
                    });
            }
            else
            {
                interfaceType = new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    KnownTypes.Lambda.ILambdaHandler.Namespace,
                    KnownTypes.Lambda.ILambdaHandler.Name,
                    new[] { lambdaFunctionEntryModel.ResponseInformation.ReturnType! });
            }

            lambdaClass.AddUsingNamespace(lambdaFunctionEntryModel.ResponseInformation.ReturnType!.Namespace);
            lambdaClass.AddBaseType(interfaceType);
        }
    }
}
