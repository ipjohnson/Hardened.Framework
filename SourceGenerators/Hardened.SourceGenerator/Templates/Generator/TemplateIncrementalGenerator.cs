using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Parser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Templates.Generator
{
    public static class TemplateIncrementalGenerator
    {
        private static readonly StringTokenNodeParser.TokenInfo _tokenInfo = new ("{{", "}}");
        private static readonly TemplateClassGenerator _generator = 
            new(
                new TemplateParseService(
                    new StringTokenNodeParser(new StringTokenNodeCreatorService())),
                new TemplateImplementationGenerator(), 
                new TemplateWhiteSpaceCleaner());

        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<ApplicationSelector.Model> entryPointProvider, ICollection<string> fileExtension)
        {
            var applicationModelCollection = entryPointProvider.Collect();

            var templateFiles = initializationContext.AdditionalTextsProvider.Where(textFile =>
            {
                var extension = Path.GetExtension(textFile.Path).TrimStart('.');
                return fileExtension.Contains(extension);
            });

            var templateModels = templateFiles.Select(GenerateTemplateModels);

            var templateModelsProvider = templateModels.Combine(applicationModelCollection);

            initializationContext.RegisterSourceOutput(templateModelsProvider, GenerateTemplateSource);

            var templateHandlerProviders = entryPointProvider.Combine(templateModels.Collect());

            initializationContext.RegisterSourceOutput(templateHandlerProviders, TemplateEntryPointGenerator.Generate);

            var helperSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.Templates.TemplateHelperAttribute);

            var templateHelperModels = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                helperSelector.Where,
                TemplateHelperModelGenerator
            );
            
            var templateHelperProviders = entryPointProvider.Combine(templateHelperModels.Collect());

            initializationContext.RegisterSourceOutput(templateHelperProviders, TemplateHelperGenerator.Generate);
        }

        private static TemplateHelperModel TemplateHelperModelGenerator(GeneratorSyntaxContext arg1, CancellationToken arg2)
        {
            if (arg1.Node is not ClassDeclarationSyntax classDeclarationSyntax)
            {
                return null;
            }

            var attribute = 
                arg1.Node.DescendantNodes().OfType<AttributeSyntax>().First(a => a.Name.ToString().Contains("TemplateHelper"));

            var helperName = attribute.ArgumentList.Arguments.First().ToString().Trim('"');

            return new TemplateHelperModel(helperName,
                TypeDefinition.Get(classDeclarationSyntax.GetNamespace(), classDeclarationSyntax.Identifier.ToString()),
                DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Singleton);
        }

        private static void GenerateTemplateSource(SourceProductionContext sourceProductionContext, (TemplateModel templateModel, ImmutableArray<ApplicationSelector.Model> applicationModels) templateData)
        {
            var templateModel = templateData.templateModel;
            var applicationModel = templateData.applicationModels.First();
            templateModel.TemplateDefinitionType = TypeDefinition.Get(
                applicationModel.ApplicationType.Namespace + ".Generated", "Template_" + templateModel.TemplateName);

            var templateSource = _generator.GenerateCSharpFile(templateModel.TemplateActionNodes,
                templateModel.TemplateName, templateModel.TemplateExtension, templateModel.TemplateDefinitionType.Namespace);

            var templateFileName = "Generated." + templateModel.TemplateDefinitionType.Name + ".cs";

            File.WriteAllText(@"C:\temp\Generated\" + templateFileName, templateSource);
            sourceProductionContext.AddSource(templateFileName, templateSource);
        }

        private static TemplateModel GenerateTemplateModels(AdditionalText additionalText, CancellationToken cancellationToken)
        {
            var extension = Path.GetExtension(additionalText.Path);
            var fileName = Path.GetFileNameWithoutExtension(additionalText.Path);

            var additionalTextString = additionalText.GetText(cancellationToken)?.ToString() ?? "";

            return new TemplateModel(
                fileName,
                extension,
                _generator.ParseCSharpFile(additionalTextString, _tokenInfo));
        }

        public class TemplateHelperModel
        {
            public TemplateHelperModel(string name, ITypeDefinition helper, DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle lifestyle)
            {
                Name = name;
                Helper = helper;
                Lifestyle = lifestyle;
            }

            public string Name { get; }

            public ITypeDefinition Helper { get; }
            
            public DependencyInjection.DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle Lifestyle
            {
                get;
            }
        }

        public class TemplateModel
        {
            public TemplateModel(string templateName, string templateExtension, IList<TemplateActionNode> templateActionNodes)
            {
                TemplateName = templateName;
                TemplateExtension = templateExtension;
                TemplateActionNodes = templateActionNodes;
            }

            public ITypeDefinition TemplateDefinitionType { get; set; }

            public IList<TemplateActionNode> TemplateActionNodes { get; }

            public string TemplateName { get; }

            public string TemplateExtension { get; }
            
        }
    }
}
