using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Configuration
{
    public static class ConfigurationPropertyImplementationGenerator
    {
        public static void Generate(SourceProductionContext arg1, ConfigurationIncrementalGenerator.ConfigurationFileModel arg2)
        {
            var csharpFile = new CSharpFileDefinition(arg2.ModelType.Namespace);

            var interfaceDefinition = new InterfaceDefinition(arg2.InterfaceType.Name);
            var modelDefinition = new ClassDefinition(arg2.ModelType.Name);

            modelDefinition.Modifiers = ComponentModifier.Partial | ComponentModifier.Public;
            modelDefinition.AddBaseType(arg2.InterfaceType);

            csharpFile.AddComponent(interfaceDefinition);
            csharpFile.AddComponent(modelDefinition);

            ProcessModelDefinition(arg2, modelDefinition, interfaceDefinition);

            var outputContext = new OutputContext();

            csharpFile.WriteOutput(outputContext);

            arg1.AddSource("ConfigurationModels_" + arg2.ModelType.Name + ".Properties.cs", outputContext.Output());
            File.WriteAllText(@"C:\temp\generated\" +arg2.InterfaceType.Namespace + "." + arg2.ModelType.Name + ".cs", outputContext.Output());
        }

        private static void ProcessModelDefinition(
            ConfigurationIncrementalGenerator.ConfigurationFileModel configurationFileModel, 
            ClassDefinition modelDefinition,
            InterfaceDefinition interfaceDefinition)
        {
            foreach (var fieldModel in configurationFileModel.FieldModels)
            {
                var property = modelDefinition.AddProperty(fieldModel.FieldType, fieldModel.PropertyName);

                property.Get.LambdaSyntax = true;
                property.Set.LambdaSyntax = true;

                property.Get.AddCode(fieldModel.Name + ";");
                property.Set.AddCode(fieldModel.Name + " = value;");

                var interfaceProperty = interfaceDefinition.AddProperty(fieldModel.FieldType, fieldModel.PropertyName);

                interfaceProperty.Set = null;
            }
        }
    }
}
