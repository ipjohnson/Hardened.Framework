using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Parser;
using Hardened.Templates.SourceGenerator.Parser;

namespace Hardened.SourceGenerator.Templates.Generator
{
    public  class  TemplateClassGenerator
    {
        private int _count = 0;
        private readonly TemplateParseService _templateParseService;
        private readonly TemplateWhiteSpaceCleaner _templateWhiteSpaceCleaner;
        private readonly TemplateImplementationGenerator _executionMethodGenerator;

        public TemplateClassGenerator(TemplateParseService templateParseService,
            TemplateImplementationGenerator executionMethodGenerator, 
            TemplateWhiteSpaceCleaner templateWhiteSpaceCleaner)
        {
            _templateParseService = templateParseService;
            _executionMethodGenerator = executionMethodGenerator;
            _templateWhiteSpaceCleaner = templateWhiteSpaceCleaner;
        }

        public IList<TemplateActionNode> ParseCSharpFile(string templateString, StringTokenNodeParser.TokenInfo tokenInfo)
        {
            var templateNodes = _templateParseService.ParseTemplate(templateString, tokenInfo);

            RemoveWhitespaces(templateNodes);

            return templateNodes;
        }

        public string GenerateCSharpFile(IList<TemplateActionNode> templateNodes, string templateName, string templateExtension, string namespaceString)
        {
            return GenerateCSharpFileFromActions(templateNodes, templateName, templateExtension, namespaceString);
        }

        private void RemoveWhitespaces(IList<TemplateActionNode> templateNodes)
        {
            _templateWhiteSpaceCleaner.RemoveWhitespace(templateNodes);
        }

        private string GenerateCSharpFileFromActions(IList<TemplateActionNode> templateNodes,
            string templateName, string templateExtension, string namespaceString)
        {
            var csharpFile = new CSharpFileDefinition(namespaceString);

            var classDefinition = csharpFile.AddClass("Template_" +  templateName);

            classDefinition.AddBaseType(KnownTypes.Templates.ITemplateExecutionHandler);

            ProcessTemplateNodes(classDefinition, templateNodes.ToList());

            CreateConstructor(classDefinition, templateName);

            _executionMethodGenerator.GenerateImplementation(classDefinition, templateExtension, templateNodes.ToList());

            var outputContext = new OutputContext();
            
            csharpFile.WriteOutput(outputContext);

            return outputContext.Output();
        }

        private void CreateConstructor(ClassDefinition classDefinition, string templateName)
        {
            classDefinition.AddField(KnownTypes.Templates.TemplateExecutionService, "_templateExecutionService");
            classDefinition.AddField(KnownTypes.Templates.IInternalTemplateServices, "_services");

            var constructor = classDefinition.AddConstructor();

            constructor.AddParameter(KnownTypes.Templates.TemplateExecutionService, "templateExecutionService");
            constructor.AddParameter(KnownTypes.Templates.IInternalTemplateServices, "services");

            constructor.AddCode("_templateExecutionService = templateExecutionService;");
            constructor.AddCode("_services = services;");

            constructor.AddCode("Initialize();");
        }

        private void ProcessTemplateNodes(ClassDefinition classDefinition, IList<TemplateActionNode> templateNodes)
        {
            foreach (var templateNode in templateNodes)
            {
                if (templateNode.Action == TemplateActionType.Content)
                {
                    if (!string.IsNullOrEmpty(templateNode.ActionText))
                    {
                        var initializeString = templateNode.ActionText;

                        initializeString = initializeString.Replace("\"", "\"\"");

                        initializeString = "@\"" + initializeString + "\"";

                        var fieldName = "_contentField" + _count++;

                        var field = classDefinition.AddField(typeof(string), fieldName);

                        field.Modifiers = ComponentModifier.Static | ComponentModifier.Readonly;
                        field.InitializeValue = initializeString;

                        templateNode.FieldName = fieldName;
                    }
                }
                else 
                {
                    ProcessTemplateNodes(classDefinition, templateNode.ArgumentList);
                    ProcessTemplateNodes(classDefinition, templateNode.ChildNodes);
                }
            }
        }
    }
}
