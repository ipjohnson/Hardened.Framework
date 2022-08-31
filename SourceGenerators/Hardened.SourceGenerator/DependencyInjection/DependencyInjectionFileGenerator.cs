using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hardened.SourceGenerator.DependencyInjection
{
    public class DependencyInjectionFileGenerator
    {
        private readonly IReadOnlyList<ITypeDefinition> _dependencies;

        public DependencyInjectionFileGenerator(IReadOnlyList<ITypeDefinition> dependencies)
        {
            _dependencies = dependencies;
        }

        public void GenerateFile(SourceProductionContext sourceProductionContext,
            (EntryPointSelector.Model Left, ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> Right) dependencyData)
        {
            var diFile = new CSharpFileDefinition(dependencyData.Left.EntryPointType.Namespace);

            GeneratedCode(dependencyData.Left, dependencyData.Right, diFile);

            var outputContext = new OutputContext();

            diFile.WriteOutput(outputContext);

            var fileName = dependencyData.Left.EntryPointType.Name + ".DependencyInjection.cs";

            File.AppendAllText(@"C:\temp\generated\" + dependencyData.Left.EntryPointType.Namespace + "." + fileName, outputContext.Output());

            sourceProductionContext.AddSource(fileName, outputContext.Output());
        }

        private void GeneratedCode(
            EntryPointSelector.Model model,
            ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
            CSharpFileDefinition diFile)
        {
            var applicationDefinition = diFile.AddClass(model.EntryPointType.Name);

            applicationDefinition.AddConstructor().Modifiers = ComponentModifier.Static;

            applicationDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            if (model.RootEntryPoint)
            {
                GenerateCreateServiceProvider(model, dependencyDataRight, applicationDefinition);
            }
            else
            {
                GenerateConfigureServiceCollection(model, dependencyDataRight, applicationDefinition);
            }
        }

        private void GenerateConfigureServiceCollection(EntryPointSelector.Model model, ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight, ClassDefinition applicationDefinition)
        {
            var providerMethod = applicationDefinition.AddMethod("ConfigureServiceCollection");

            providerMethod.Modifiers |= ComponentModifier.Private;

            var environment = providerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollectionDefinition =
                providerMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

            GenerateRegistrationStatements(model, dependencyDataRight, providerMethod, environment, serviceCollectionDefinition);
        }

        private void GenerateCreateServiceProvider(EntryPointSelector.Model model, ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
            ClassDefinition applicationDefinition)
        {
            var providerMethod = applicationDefinition.AddMethod("CreateServiceProvider");
            providerMethod.SetReturnType(KnownTypes.DI.IServiceProvider);

            var environment = providerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");

            var overrideDependenciesDefinition = providerMethod.AddParameter(
                TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            ParameterDefinition loggerFactory
                = providerMethod.AddParameter(KnownTypes.Logging.ILoggerFactory, "loggerFactory");

            providerMethod.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

            var serviceCollectionDefinition =
                providerMethod.Assign(New(KnownTypes.DI.ServiceCollection)).ToVar("serviceCollection");

            providerMethod.NewLine();

            providerMethod.AddIndentedStatement(
                    serviceCollectionDefinition.Invoke(
                        "TryAddTransient", "typeof(ILogger<>)", "typeof(LoggerImpl<>)"));
            providerMethod.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeLogging);

            providerMethod.AddIndentedStatement(
                serviceCollectionDefinition.InvokeGeneric(
                    "AddSingleton", new[] { KnownTypes.Logging.ILoggerFactory }, "_ => loggerFactory"));

            providerMethod.NewLine();

            GenerateRegistrationStatements(model, dependencyDataRight, providerMethod, environment, serviceCollectionDefinition);

            providerMethod.AddIndentedStatement("overrideDependencies?.Invoke(serviceCollection)");

            providerMethod.NewLine();

            providerMethod.Return(serviceCollectionDefinition.Invoke("BuildServiceProvider"));
        }

        private void GenerateRegistrationStatements(EntryPointSelector.Model model, ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
            MethodDefinition providerMethod, ParameterDefinition environment, InstanceDefinition serviceCollectionDefinition)
        {
            foreach (var typeDefinition in _dependencies)
            {
                providerMethod.AddIndentedStatement(
                    Invoke(typeDefinition, "Register", environment, serviceCollectionDefinition));
            }

            providerMethod.NewLine();

            var moduleMethod = model.MethodDefinitions.FirstOrDefault(m => m.Name == "Modules");
            
            if (moduleMethod != null)
            {
                var moduleInvoke =
                    moduleMethod.Parameters.Count == 0 ? Invoke("Modules") : Invoke("Modules", environment);

                providerMethod.AddIndentedStatement(Invoke(
                    KnownTypes.DI.Registry.StandardDependencies,
                    "ProcessModules",
                    environment, 
                    serviceCollectionDefinition,
                    moduleInvoke));
            }

            providerMethod.NewLine();

            providerMethod.AddCode("{arg1}<[arg2]>.ApplyRegistration(environment, serviceCollection, this);",
                KnownTypes.DI.DependencyRegistry, model.EntryPointType.Name);

            providerMethod.NewLine();

            foreach (var serviceModel in dependencyDataRight)
            {
                var registerMethod = "AddTransient";

                switch (serviceModel.Lifestyle)
                {
                    case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Scoped:
                        registerMethod = "AddScoped";
                        break;
                    case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Singleton:
                        registerMethod = "AddSingleton";
                        break;
                }

                if (serviceModel.Try)
                {
                    registerMethod = "Try" + registerMethod;
                }

                providerMethod.AddIndentedStatement(
                    serviceCollectionDefinition.InvokeGeneric(registerMethod,
                        new[] { serviceModel.ServiceType, serviceModel.ImplementationType }));
            }

            providerMethod.NewLine();

            var method = model.MethodDefinitions.FirstOrDefault(m => m.Name == "RegisterDependencies");

            if (method != null)
            {
                providerMethod.AddIndentedStatement(method.Name + "(serviceCollection)");
                providerMethod.NewLine();
            }
        }
    }
}
