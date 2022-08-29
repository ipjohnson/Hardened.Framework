using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public static class ApplicationFileWriter
    {
        public static string WriteFile(EntryPointSelector.Model entryPoint)
        {
            var applicationFile = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);

            CreateApplicationClass(applicationFile, entryPoint);

            var context = new OutputContext();

            applicationFile.WriteOutput(context);

            return context.Output();
        }

        private static void CreateApplicationClass(CSharpFileDefinition applicationFile, EntryPointSelector.Model entryPoint)
        {
            var appClass = applicationFile.AddClass(entryPoint.EntryPointType.Name);

            appClass.Modifiers |= ComponentModifier.Partial;

            appClass.AddBaseType(KnownTypes.Application.IApplicationRoot);
            
            var provider = appClass.AddProperty(KnownTypes.DI.IServiceProvider, "Provider");

            provider.Set.Modifiers |= ComponentModifier.Private;

            CreateConstructors(appClass, entryPoint, provider.Instance);
        }

    
        private static void CreateConstructors(ClassDefinition appClass,
            EntryPointSelector.Model entryPoint,
            InstanceDefinition providerInstanceDefinition)
        {
            appClass.AddConstructor(This(New(KnownTypes.Application.EnvironmentImpl), Null()));

            var constructor = appClass.AddConstructor();

            var environment = constructor.AddParameter(KnownTypes.Application.IEnvironment, "environment");

            var overrides =
                constructor.AddParameter(TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            var loggerFactory = SetupLoggerFactory(entryPoint, constructor, environment);

            constructor.Assign(Invoke("CreateServiceProvider", environment, overrides, loggerFactory)).To(providerInstanceDefinition);

            var startupMethod = "null";

            if (entryPoint.MethodDefinitions.Any(m => m.Name == "Startup"))
            {
                startupMethod = "Startup";
            }

            constructor.AddIndentedStatement(
                Invoke(
                    KnownTypes.Application.ApplicationLogic,
                    "StartWithWait",
                    providerInstanceDefinition,
                    startupMethod,
                    15));
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
    }
}
