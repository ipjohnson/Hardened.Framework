using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Configuration
{
    public static class ConfigurationEntryPointGenerator
    {
        public static void Generate(SourceProductionContext arg1, 
            (ApplicationSelector.Model AppModel, ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> ConfigFiles) arg2)
        {
            var cSharpFile = new CSharpFileDefinition(arg2.AppModel.ApplicationType.Namespace);

            var classDefinition = cSharpFile.AddClass(arg2.AppModel.ApplicationType.Name);

            classDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            GenerateConfiguration(classDefinition, arg2.AppModel, arg2.ConfigFiles);

            var outputContext = new OutputContext();

            cSharpFile.WriteOutput(outputContext);

            arg1.AddSource(arg2.AppModel.ApplicationType.Name + ".Configuration.cs", outputContext.Output());

            File.AppendAllText(@"C:\temp\generated\Application.Configration.cs", outputContext.Output());
        }

        private static void GenerateConfiguration(
            ClassDefinition classDefinition,
            ApplicationSelector.Model entryPoint, 
            ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> configFiles)
        {
            var providerType = GenerateProviderType(classDefinition, entryPoint, configFiles);

            GenerateDependencyInjectionRegistration(classDefinition, entryPoint, configFiles, providerType);
        }

        private static void GenerateDependencyInjectionRegistration(
            ClassDefinition classDefinition, 
            ApplicationSelector.Model entryPoint, 
            ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> configFiles, 
            ITypeDefinition providerType)
        {
            classDefinition.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeConfiguration);

            var templateField = classDefinition.AddField(typeof(int), "_configDi");

            templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
            templateField.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeDependencyInjection);
            templateField.InitializeValue = $"DependencyRegistry<{classDefinition.Name}>.Register(ConfigurationDI)";

            var diMethod = classDefinition.AddMethod("ConfigurationDI");

            diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

            var environment = diMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
            var entryPointDef = diMethod.AddParameter(entryPoint.ApplicationType, "entryPoint");

            foreach (var configurationFileModel in configFiles)
            {
                var ioptionsType =
                    new GenericTypeDefinition(TypeDefinitionEnum.ClassDefinition, "IOptions",
                        "Microsoft.Extensions.Options", new[] { configurationFileModel.InterfaceType });

                var providerString =
                    $"serviceProvider => Microsoft.Extensions.Options.Options.Create(serviceProvider.GetRequiredService<IConfigurationManager>().GetConfiguration<{configurationFileModel.InterfaceType.Name}>())";
                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                    new[] { ioptionsType }, providerString));
            }

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Application.IEnvironment }, environment));
            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Configuration.IConfigurationPackage, providerType }));
        }

        private static ITypeDefinition GenerateProviderType(
            ClassDefinition classDefinition, 
            ApplicationSelector.Model entryPoint, 
            ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> configFiles)
        {
            var configurationProvider = classDefinition.AddClass("ConfigurationProvider");

            configurationProvider.AddBaseType(KnownTypes.Configuration.IConfigurationPackage);

            var providerMethod = configurationProvider.AddMethod("ConfigurationValueProviders");

            providerMethod.SetReturnType(
                TypeDefinition.IEnumerable(KnownTypes.Configuration.IConfigurationValueProvider));
            
            foreach (var configurationFileModel in configFiles)
            {
                providerMethod.AddUsingNamespace(configurationFileModel.ModelType.Namespace);
                providerMethod.AddIndentedStatement(
                    $"yield return new NewConfigurationValueProvider<{configurationFileModel.InterfaceType.Name}, {configurationFileModel.ModelType.Name}>()");
            }

            var amendersMethod = configurationProvider.AddMethod("Amenders");

            amendersMethod.SetReturnType(
                TypeDefinition.IEnumerable(KnownTypes.Configuration.IConfigurationValueAmender));

            amendersMethod.AddIndentedStatement("yield break");

            return TypeDefinition.Get(entryPoint.ApplicationType.Namespace, entryPoint.ApplicationType.Name + ".ConfigurationProvider");
        }
    }
}
