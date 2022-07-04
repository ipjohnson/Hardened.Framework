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
            (ApplicationSelector.Model Left, ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> Right) dependencyData)
        {
            var diFile = new CSharpFileDefinition(dependencyData.Left.ApplicationType.Namespace);

            GeneratedCode(dependencyData.Left, dependencyData.Right, diFile);

            var outputContext = new OutputContext();

            diFile.WriteOutput(outputContext);

            var fileName = dependencyData.Left.ApplicationType.Name + ".DependencyInjection.cs";

            File.WriteAllText(@"C:\temp\generated\" + fileName, outputContext.Output());

            sourceProductionContext.AddSource(fileName, outputContext.Output());
        }

        private void GeneratedCode(
            ApplicationSelector.Model model,
            ImmutableArray<DependencyInjectionIncrementalGenerator.ServiceModel> dependencyDataRight, 
            CSharpFileDefinition diFile)
        {
            var applicationDefinition = diFile.AddClass(model.ApplicationType.Name);

            applicationDefinition.AddConstructor().Modifiers = ComponentModifier.Static;

            applicationDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            var providerMethod = applicationDefinition.AddMethod("CreateServiceProvider");
            providerMethod.SetReturnType(KnownTypes.DI.IServiceProvider);

            var overrideDependenciesDefinition = providerMethod.AddParameter(
                TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            providerMethod.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

            var serviceCollectionDefinition =
                providerMethod.Assign(New(KnownTypes.DI.ServiceCollection)).ToVar("serviceCollection");

            providerMethod.NewLine();

            foreach (var typeDefinition in _dependencies)
            {
                providerMethod.AddIndentedStatement(Invoke(typeDefinition, "Register", serviceCollectionDefinition));
            }

            providerMethod.NewLine();

            providerMethod.AddCode("{arg1}<[arg2]>.ApplyRegistration(serviceCollection);",
                KnownTypes.DI.DependencyRegistry, model.ApplicationType.Name);

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

                providerMethod.AddIndentedStatement(
                    serviceCollectionDefinition.InvokeGeneric(registerMethod, 
                        new []{ serviceModel.ServiceType, serviceModel.ImplementationType}));
            }

            providerMethod.NewLine();

            providerMethod.AddIndentedStatement("overrideDependencies?.Invoke(serviceCollection)");
            
            providerMethod.NewLine();
            
            providerMethod.Return(serviceCollectionDefinition.Invoke("BuildServiceProvider"));
        }
    }
}
