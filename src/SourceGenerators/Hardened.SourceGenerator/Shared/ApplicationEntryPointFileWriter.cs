using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models;

namespace Hardened.SourceGenerator.Shared
{
    /// <summary>
    /// Default entry point writer
    /// </summary>
    public abstract class ApplicationEntryPointFileWriter
    {
        protected const string RootServiceProvider = "RootServiceProvider";

        public virtual void CreateApplicationClass(EntryPointSelector.Model model,
            IConstructContainer constructContainer)
        {
            var classDefinition = CreateClassDefinition(model, constructContainer);

            ImplementApplicationRoot(model, classDefinition);

            CreateConstructors(model, classDefinition);

            CreateDomainMethods(model, classDefinition);
        }

        protected virtual void ImplementApplicationRoot(EntryPointSelector.Model model, ClassDefinition classDefinition)
        {
            classDefinition.ImplementApplicationRoot();
        }

        protected virtual void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition)
        {
            
        }

        protected virtual ClassDefinition CreateClassDefinition(EntryPointSelector.Model model, IConstructContainer constructContainer)
        {
            var classDefinition = constructContainer.AddClass(model.EntryPointType.Name);

            classDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            return classDefinition;
        }

        protected virtual void CreateConstructors(
            EntryPointSelector.Model entryPoint, ClassDefinition appClass)
        {
            appClass.AddConstructor(This(New(KnownTypes.Application.EnvironmentImpl), Null()));

            var constructor = appClass.AddConstructor();

            var environment = constructor.AddParameter(KnownTypes.Application.IEnvironment, "environment");

            var overrides =
                constructor.AddParameter(TypeDefinition.Action(KnownTypes.Application.IEnvironment, KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            var loggerFactory = SetupLoggerFactory(entryPoint, constructor, environment);

            constructor.Assign(Invoke("CreateServiceProvider", environment, overrides, loggerFactory, "RegisterInitDi")).To("RootServiceProvider");

            var registerInitDi = appClass.AddMethod("RegisterInitDi");

            registerInitDi.Modifiers = ComponentModifier.Private | ComponentModifier.Static;
            var env = registerInitDi.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var coll = registerInitDi.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

            foreach (var typeDefinition in RegisterDiTypes())
            {
                registerInitDi.AddIndentedStatement(
                    Invoke(typeDefinition, "Register", env, coll));
            }
            
            var startupMethod = "null";

            if (entryPoint.MethodDefinitions.Any(m => m.Name == "Startup"))
            {
                startupMethod = "Startup";
            }

            constructor.AddIndentedStatement(
                Invoke(
                    KnownTypes.Application.ApplicationLogic,
                    "StartWithWait",
                    "RootServiceProvider",
                    startupMethod,
                    15));

            CustomConstructorLogic(entryPoint, appClass, constructor, environment);
        }

        protected virtual IEnumerable<ITypeDefinition> RegisterDiTypes()
        {
            yield break;
        }

        protected virtual void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass, ConstructorDefinition constructor, ParameterDefinition environment)
        {
            
        }

        protected virtual InstanceDefinition SetupLoggerFactory(
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
                    $"LoggerFactory.Create({LoggerHelper.Name}.CreateAction(ConfigureLogLevel(environment), \"{entryPoint.EntryPointType.Namespace}\"))");
                logCreateMethod.AddUsingNamespace(LoggerHelper.Namespace);
            }
            else
            {
                logCreateMethod = CodeOutputComponent.Get(
                    $"LoggerFactory.Create({LoggerHelper.Name}.CreateAction(environment, \"{entryPoint.EntryPointType.Namespace}\"))");
                logCreateMethod.AddUsingNamespace(LoggerHelper.Namespace);
            }

            logCreateMethod.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.Logging);

            return constructorDefinition.Assign(logCreateMethod).ToVar("loggerFactory");
        }
        
        protected abstract ITypeDefinition LoggerHelper { get; }
    }
}
