using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Configuration
{
    public static class ConfigurationEntryPointGenerator
    {
        public static void Generate(SourceProductionContext arg1, 
            (EntryPointSelector.Model AppModel, ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> ConfigFiles) arg2)
        {
            var cSharpFile = new CSharpFileDefinition(arg2.AppModel.EntryPointType.Namespace);

            var classDefinition = cSharpFile.AddClass(arg2.AppModel.EntryPointType.Name);

            classDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            GenerateConfiguration(classDefinition, arg2.AppModel, arg2.ConfigFiles);

            var outputContext = new OutputContext();

            cSharpFile.WriteOutput(outputContext);

            arg1.AddSource(arg2.AppModel.EntryPointType.Name + ".Configuration.cs", outputContext.Output());
        }

        private static void GenerateConfiguration(
            ClassDefinition classDefinition,
            EntryPointSelector.Model entryPoint, 
            ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> configFiles)
        {
            var providerType = GenerateProviderType(classDefinition, entryPoint, configFiles);

            GenerateDependencyInjectionRegistration(classDefinition, entryPoint, configFiles, providerType);
        }

        private static void GenerateDependencyInjectionRegistration(
            ClassDefinition classDefinition, 
            EntryPointSelector.Model entryPoint, 
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
            var entryPointDef = diMethod.AddParameter(entryPoint.EntryPointType, "entryPoint");

            foreach (var configurationFileModel in configFiles)
            {
                var ioptionsType =
                    new GenericTypeDefinition(TypeDefinitionEnum.ClassDefinition, 
                        "Microsoft.Extensions.Options", "IOptions", new[] { configurationFileModel.InterfaceType });

                var providerString =
                    $"serviceProvider => Microsoft.Extensions.Options.Options.Create(serviceProvider.GetRequiredService<IConfigurationManager>().GetConfiguration<{configurationFileModel.InterfaceType.Name}>())";
                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                    new[] { ioptionsType }, providerString));
            }

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Application.IEnvironment }, environment));
            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Configuration.IConfigurationPackage, providerType }));

            var configureMethod = entryPoint.MethodDefinitions.FirstOrDefault(m => m.Name == "Configure");

            if (configureMethod != null)
            {
                diMethod.NewLine();
                var fluentConfig = diMethod.Assign(New(KnownTypes.Configuration.AppConfig)).ToVar("fluentConfig");
                diMethod.AddIndentedStatement(entryPointDef.Invoke("Configure", fluentConfig));
                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                    new[] { KnownTypes.Configuration.IConfigurationPackage }, fluentConfig));
            }
        }

        private static ITypeDefinition GenerateProviderType(
            ClassDefinition classDefinition, 
            EntryPointSelector.Model entryPoint, 
            ImmutableArray<ConfigurationIncrementalGenerator.ConfigurationFileModel> configFiles)
        {
            var configurationProvider = classDefinition.AddClass("ConfigurationProvider");

            configurationProvider.AddBaseType(KnownTypes.Configuration.IConfigurationPackage);

            var providerMethod = configurationProvider.AddMethod("ConfigurationValueProviders");

            providerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            providerMethod.SetReturnType(
                TypeDefinition.IEnumerable(KnownTypes.Configuration.IConfigurationValueProvider));
            
            foreach (var configurationFileModel in configFiles)
            {
                var initConfigValue = CreateInitConfigValueMethod(classDefinition, configurationFileModel);

                providerMethod.AddUsingNamespace(configurationFileModel.ModelType.Namespace);
                providerMethod.AddIndentedStatement(
                    $"yield return new NewConfigurationValueProvider<{configurationFileModel.InterfaceType.Name}, {configurationFileModel.ModelType.Name}>({initConfigValue})");
            }

            if (configFiles.Length == 0)
            {
                providerMethod.AddIndentedStatement("yield break");
            }

            var amendersMethod = configurationProvider.AddMethod("ConfigurationValueAmenders");

            amendersMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            amendersMethod.SetReturnType(
                TypeDefinition.IEnumerable(KnownTypes.Configuration.IConfigurationValueAmender));

            amendersMethod.AddIndentedStatement("yield break");

            return TypeDefinition.Get(entryPoint.EntryPointType.Namespace, entryPoint.EntryPointType.Name + ".ConfigurationProvider");
        }

        private static string CreateInitConfigValueMethod(ClassDefinition classDefinition, ConfigurationIncrementalGenerator.ConfigurationFileModel configurationFileModel)
        {
            var modelArray = 
                configurationFileModel.FieldModels.Where(m => !string.IsNullOrEmpty(m.FromEnvironmentVariable))
                .ToArray();

            if (modelArray.Length > 0)
            {
                var methodName = "Configure" + configurationFileModel.ModelType.Name;

                var method = classDefinition.AddMethod(methodName);

                method.Modifiers = ComponentModifier.Private | ComponentModifier.Static;
                var env = method.AddParameter(KnownTypes.Application.IEnvironment, "environment");
                var model = method.AddParameter(configurationFileModel.ModelType, "model");

                foreach (var configurationFieldModel in modelArray)
                {
                    var propertyAccess = model.Property(configurationFieldModel.PropertyName);
                    var invoke = env.Invoke("Value", QuoteString(configurationFieldModel.FromEnvironmentVariable), propertyAccess);
                    
                    method.Assign(invoke).To(propertyAccess);
                }

                return methodName;
            }

            return "null";
        }
    }
}
