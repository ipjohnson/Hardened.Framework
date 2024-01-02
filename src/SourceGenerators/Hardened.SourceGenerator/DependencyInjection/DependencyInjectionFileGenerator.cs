﻿using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.DependencyInjection;

public class DependencyInjectionFileGenerator {
    private readonly IReadOnlyList<ITypeDefinition> _dependencies;

    public DependencyInjectionFileGenerator(IReadOnlyList<ITypeDefinition> dependencies) {
        _dependencies = dependencies;
    }

    public void GenerateFile(
        SourceProductionContext sourceProductionContext,
        (EntryPointSelector.Model Left,
            ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> Right)
            dependencyData) {
        var diFile = new CSharpFileDefinition(dependencyData.Left.EntryPointType.Namespace);

        GeneratedCode(dependencyData.Left, dependencyData.Right, diFile);

        var outputContext = new OutputContext();

        diFile.WriteOutput(outputContext);

        var fileName = dependencyData.Left.EntryPointType.Name + ".DependencyInjection.cs";

        sourceProductionContext.AddSource(fileName, outputContext.Output());
    }

    private void GeneratedCode(
        EntryPointSelector.Model model,
        ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
        CSharpFileDefinition diFile) {
        var applicationDefinition = diFile.AddClass(model.EntryPointType.Name);

        applicationDefinition.AddConstructor().Modifiers = ComponentModifier.Static;

        applicationDefinition.AddBaseType(KnownTypes.Application.IApplicationModule);

        applicationDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        GenerateCreateServiceProvider(model, dependencyDataRight, applicationDefinition);

        GenerateConfigureServiceCollection(model, dependencyDataRight, applicationDefinition);
    }

    private void GenerateConfigureServiceCollection(
        EntryPointSelector.Model model,
        ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
        ClassDefinition applicationDefinition) {
        var providerMethod = applicationDefinition.AddMethod("ConfigureModule");

        providerMethod.Modifiers |= ComponentModifier.Public;

        var environment =
            providerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
        var serviceCollectionDefinition =
            providerMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        GenerateRegistrationStatements(model, dependencyDataRight, providerMethod, environment,
            serviceCollectionDefinition);
    }

    private void GenerateCreateServiceProvider(
        EntryPointSelector.Model model,
        ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
        ClassDefinition applicationDefinition) {
        var providerMethod = applicationDefinition.AddMethod("CreateServiceProvider");
        providerMethod.Modifiers = ComponentModifier.Public;

        providerMethod.SetReturnType(KnownTypes.DI.ServiceProvider);

        var environment =
            providerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");

        var overrideDependenciesDefinition = providerMethod.AddParameter(
            TypeDefinition
                .Action(KnownTypes.Application.IEnvironment, KnownTypes.DI.IServiceCollection)
                .MakeNullable(),
            "overrideDependencies");

        ParameterDefinition loggingBuilderAction
            = providerMethod.AddParameter(
                TypeDefinition.Action(KnownTypes.Logging.ILoggingBuilder).MakeNullable(),
                "loggingBuilderAction");

        var initAction = providerMethod.AddParameter(
            TypeDefinition
                .Action(KnownTypes.Application.IEnvironment, KnownTypes.DI.IServiceCollection)
                .MakeNullable(),
            "initDependencies");

        initAction.DefaultValue = Null();

        providerMethod.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

        var serviceCollectionDefinition =
            providerMethod.Assign(New(KnownTypes.DI.ServiceCollection)).ToVar("serviceCollection");

        providerMethod.NewLine();

        var loggerStatement = NullCoalesce(loggingBuilderAction, "(b => {})");

        loggerStatement.PrintParentheses = false;
        loggerStatement.Indented = false;

        providerMethod.AddIndentedStatement(
            serviceCollectionDefinition.Invoke(
                "AddLogging", loggerStatement));

        providerMethod.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.Logging);

        providerMethod.AddIndentedStatement(
            serviceCollectionDefinition.Invoke("AddSingleton", "environment")
        );

        providerMethod.NewLine();

        providerMethod.AddIndentedStatement(
            "initDependencies?.Invoke(environment, serviceCollection)");

        providerMethod.NewLine();

        providerMethod.AddIndentedStatement(Invoke("ConfigureModule", environment,
            serviceCollectionDefinition));

        providerMethod.AddIndentedStatement(
            "overrideDependencies?.Invoke(environment, serviceCollection)");

        providerMethod.NewLine();

        providerMethod.Return(serviceCollectionDefinition.Invoke("BuildServiceProvider"));
    }

    private void GenerateRegistrationStatements(
        EntryPointSelector.Model model,
        ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight,
        MethodDefinition providerMethod,
        ParameterDefinition environment,
        InstanceDefinition serviceCollectionDefinition) {
        var dependencyType = new GenericTypeDefinition(TypeDefinitionEnum.ClassDefinition,
            KnownTypes.DI.DependencyRegistry.Namespace,
            KnownTypes.DI.DependencyRegistry.Name,
            new[] { model.EntryPointType }
        );
        
        var methodBody = providerMethod.If(new StaticInvokeStatement(dependencyType,
            "ShouldRegisterModule", new[] { serviceCollectionDefinition }){Indented = false});

        foreach (var typeDefinition in _dependencies) {
            methodBody.AddIndentedStatement(
                Invoke(typeDefinition, "Register", environment, serviceCollectionDefinition));
        }

        methodBody.NewLine();

        var moduleMethod = model.MethodDefinitions.FirstOrDefault(m => m.Name == "Modules");

        if (moduleMethod != null) {
            var moduleInvoke =
                moduleMethod.Parameters.Count == 0
                    ? Invoke("Modules")
                    : Invoke("Modules", environment);

            methodBody.AddIndentedStatement(Invoke(
                KnownTypes.DI.Registry.StandardDependencies,
                "ProcessModules",
                environment,
                serviceCollectionDefinition,
                moduleInvoke));
        }

        methodBody.NewLine();

        var applyRegistration = new StaticInvokeStatement(dependencyType, "ApplyRegistration",
            new IOutputComponent[] {
                environment, 
                serviceCollectionDefinition, 
                new CodeOutputComponent("this") {Indented = false}
            }) {Indented = false};
        
        methodBody.AddIndentedStatement(applyRegistration);

        methodBody.NewLine();

        foreach (var serviceModel in
                 dependencyDataRight.Sort((x, y) =>
                     Comparer<int>.Default.Compare(x.Environments.Count, y.Environments.Count))) {
            var registerMethod = "AddTransient";

            switch (serviceModel.Lifestyle) {
                case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Scoped:
                    registerMethod = "AddScoped";
                    break;
                case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle
                    .Singleton:
                    registerMethod = "AddSingleton";
                    break;
            }

            if (serviceModel.Try) {
                registerMethod = "Try" + registerMethod;
            }

            BaseBlockDefinition block = methodBody;

            if (serviceModel.Environments.Count > 0) {
                var environments = serviceModel.Environments.Select(QuoteString).OfType<object>()
                    .ToArray();

                var matches = environment.Invoke("Matches", environments);

                block = methodBody.If(matches);
            }

            File.AppendAllText(@"C:\temp\generator.txt", $"serviceType: {serviceModel.ServiceType.Name} {serviceModel.ServiceType.TypeArguments.Count}\n");
            
            if (serviceModel.ServiceType.TypeArguments.Count > 0) {
                foreach (var definition in serviceModel.ServiceType.TypeArguments) {


                    File.AppendAllText(@"C:\temp\generator.txt",
                        $"close: {definition.Name} {definition.GetType().Name} {TypeDefinition.Get(typeof(int))}\n");
                }
            }
            
            block.AddIndentedStatement(serviceCollectionDefinition.Invoke(registerMethod,
                    TypeOf(serviceModel.ServiceType), TypeOf(serviceModel.ImplementationType)));
        }

        methodBody.NewLine();

        var method = model.MethodDefinitions.FirstOrDefault(m => m.Name == "RegisterDependencies");

        if (method != null) {
            if (method.Parameters.Count == 2 || method.Parameters.Count == 1) {
                var parameters = "";

                foreach (var parameterDefinition in method.Parameters) {
                    if (parameters != "") {
                        parameters += ", ";
                    }

                    if (parameterDefinition.Type.Equals(KnownTypes.DI.IServiceCollection)) {
                        parameters += serviceCollectionDefinition.Name;
                    } else if (
                        parameterDefinition.Type.Equals(KnownTypes.Application.IEnvironment)) {
                        parameters += environment.Name;
                    } else {
                        throw new Exception(
                            $"Incorrect parameter type {parameterDefinition.Type.Name} for RegisterDependencies method");
                    }
                }

                methodBody.AddIndentedStatement($"{method.Name}({parameters})");    
            }
            else {
                throw new Exception("Incorrect number of parameters for RegisterDependencies");
            }
            
            methodBody.NewLine();
        }
    }
}