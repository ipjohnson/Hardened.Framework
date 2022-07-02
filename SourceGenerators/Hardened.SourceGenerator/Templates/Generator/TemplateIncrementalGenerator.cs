using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Parser;
using Microsoft.CodeAnalysis;

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
            string libraryAttribute, ICollection<string> fileExtension)
        {
            var applicationModel = initializationContext.GetEntryPoint(libraryAttribute);

            var applicationModelCollection = applicationModel.Collect();

            var templateFiles = initializationContext.AdditionalTextsProvider.Where(textFile =>
            {
                var extension = Path.GetExtension(textFile.Path).TrimStart('.');
                return fileExtension.Contains(extension);
            });

            var templateModels = templateFiles.Select(GenerateTemplateModels);

            var templateModelsProvider = templateModels.Combine(applicationModelCollection);

            initializationContext.RegisterSourceOutput(templateModelsProvider, GenerateTemplateSource);

            var templateHandlerProviders = applicationModel.Combine(templateModels.Collect());

            initializationContext.RegisterSourceOutput(templateHandlerProviders, TemplateApplicationPartialGenerator.Generate);
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
