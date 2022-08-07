using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Configuration
{
    public static class ConfigurationIncrementalGenerator
    {
        public static void Setup(
            IncrementalGeneratorInitializationContext initializationContext, 
            IncrementalValuesProvider<ApplicationSelector.Model> entryPointProvider)
        {
            var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.Configuration.ConfigurationModelAttribute);

            var configurationFileModels = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                classSelector.Where,
                GenerateConfigurationFileModel
            );

            initializationContext.RegisterSourceOutput(configurationFileModels, ConfigurationPropertyImplementationGenerator.Generate);

            var modelCollection = configurationFileModels.Collect();
            initializationContext.RegisterSourceOutput(entryPointProvider.Combine(modelCollection),
                ConfigurationEntryPointGenerator.Generate);
        }

        private static ConfigurationFileModel GenerateConfigurationFileModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;

            File.AppendAllText(@"C:\temp\generated\ConfigurationModels.txt", $"{classDeclarationSyntax.Identifier.ValueText}\r\n");

            var classTypeDef = TypeDefinition.Get(classDeclarationSyntax.GetNamespace(),
                classDeclarationSyntax.Identifier.ToString());

            var interfaceDef = TypeDefinition.Get(classDeclarationSyntax.GetNamespace(),
                "I" + classDeclarationSyntax.Identifier);

            var fieldModels = new List<ConfigurationFieldModel>();

            foreach (var memberDeclarationSyntax in classDeclarationSyntax.Members)
            {
                if (memberDeclarationSyntax is FieldDeclarationSyntax fieldDeclarationSyntax)
                {
                    foreach (var variableDeclaratorSyntax in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        var name = variableDeclaratorSyntax.Identifier.ValueText;
                        
                        var fieldType = fieldDeclarationSyntax.Declaration.Type.GetTypeDefinition(context);

                        if (fieldType != null)
                        {
                            fieldModels.Add(new ConfigurationFieldModel(fieldType, name, PropertyNameFrom(name)));
                        }
                    }
                }
            }

            return new ConfigurationFileModel(classTypeDef, interfaceDef, fieldModels);
        }

        private static string PropertyNameFrom(string name)
        {
            name = name.TrimStart('_');

            if (name.Length > 1)
            {
                return char.ToUpperInvariant(name[0]) + name.Substring(1);
            }

            return name.ToUpperInvariant();
        }

        public class ConfigurationFileModel
        {
            public ConfigurationFileModel(ITypeDefinition modelType, ITypeDefinition interfaceType, IEnumerable<ConfigurationFieldModel> fieldModels)
            {
                ModelType = modelType;
                FieldModels = fieldModels;
                InterfaceType = interfaceType;
            }

            public ITypeDefinition ModelType { get; }

            public ITypeDefinition InterfaceType { get; }

            public IEnumerable<ConfigurationFieldModel> FieldModels { get; }
        }

        public class ConfigurationFieldModel
        {
            public ConfigurationFieldModel(ITypeDefinition fieldType, string name, string propertyName)
            {
                FieldType = fieldType;
                Name = name;
                PropertyName = propertyName;
            }

            public ITypeDefinition FieldType { get; }

            public string Name { get; }

            public string PropertyName { get; }
        }
    }
}
