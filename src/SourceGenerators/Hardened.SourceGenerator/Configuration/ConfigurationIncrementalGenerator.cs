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
            IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider)
        {
            var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.Configuration.ConfigurationModelAttribute);

            var configurationFileModels = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                classSelector.Where,
                GenerateConfigurationFileModel
            ).WithComparer(new ConfigurationFileModelComparer());

            initializationContext.RegisterSourceOutput(configurationFileModels, ConfigurationPropertyImplementationGenerator.Generate);

            var modelCollection = configurationFileModels.Collect();
            initializationContext.RegisterSourceOutput(entryPointProvider.Combine(modelCollection),
                ConfigurationEntryPointGenerator.Generate);
        }

        private static ConfigurationFileModel GenerateConfigurationFileModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)context.Node;
            
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
                            var fromEnvVarString = "";

                            var fromEnvVar =
                                fieldDeclarationSyntax.GetAttribute("FromEnvironmentVariable");

                            if (fromEnvVar != null)
                            {
                                fromEnvVarString = 
                                    fromEnvVar.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "";
                            }

                            var model =
                                new ConfigurationFieldModel(fieldType, name, PropertyNameFrom(name), fromEnvVarString);

                            fieldModels.Add(model);
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
            public ConfigurationFileModel(ITypeDefinition modelType, ITypeDefinition interfaceType, IReadOnlyList<ConfigurationFieldModel> fieldModels)
            {
                ModelType = modelType;
                FieldModels = fieldModels;
                InterfaceType = interfaceType;
            }

            public ITypeDefinition ModelType { get; }

            public ITypeDefinition InterfaceType { get; }

            public IReadOnlyList<ConfigurationFieldModel> FieldModels { get; }

            public override bool Equals(object obj)
            {
                if (obj is not ConfigurationFileModel model)
                {
                    return false;
                }

                if (!ModelType.Equals(model.ModelType))
                {
                    return false;
                }

                if (!InterfaceType.Equals(model.InterfaceType))
                {
                    return false;
                }

                return FieldModels.DeepEquals(model.FieldModels);
            }
            
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = ModelType.GetHashCode();
                    hashCode = (hashCode * 397) ^ InterfaceType.GetHashCode();
                    hashCode = (hashCode * 397) ^ FieldModels.GetHashCodeAggregation();
                    return hashCode;
                }
            }
        }

        public class ConfigurationFieldModel
        {
            public ConfigurationFieldModel(ITypeDefinition fieldType, string name, string propertyName, string fromEnvironmentVariable)
            {
                FieldType = fieldType;
                Name = name;
                PropertyName = propertyName;
                FromEnvironmentVariable = fromEnvironmentVariable;
            }

            public ITypeDefinition FieldType { get; }

            public string Name { get; }

            public string PropertyName { get; }

            public string FromEnvironmentVariable { get; }

            public override bool Equals(object obj)
            {
                if (obj is not ConfigurationFieldModel configurationFieldModel)
                {
                    return false;
                }
                return FieldType.Equals(configurationFieldModel.FieldType) &&
                       Name.Equals(configurationFieldModel.Name) &&
                       PropertyName.Equals(configurationFieldModel.PropertyName) && 
                       FromEnvironmentVariable.Equals(configurationFieldModel.FromEnvironmentVariable);
            }
            
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = FieldType.GetHashCode();

                    hashCode = (hashCode * 397) ^ Name.GetHashCode();
                    hashCode = (hashCode * 397) ^ PropertyName.GetHashCode();
                    hashCode = (hashCode * 397) ^ FromEnvironmentVariable.GetHashCode();

                    return hashCode;
                }
            }
        }
    }
}
