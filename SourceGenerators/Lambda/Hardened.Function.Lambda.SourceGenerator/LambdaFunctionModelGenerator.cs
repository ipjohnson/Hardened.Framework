using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public static class LambdaFunctionModelGenerator
    {
        public static RequestHandlerModel GenerateRequestModel(GeneratorSyntaxContext context, CancellationToken cancellation)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;

            var methodName = GetControllerMethod(methodDeclaration);
            var controllerType = GetControllerType(methodDeclaration);
            var response = GetResponseInformation(context, methodDeclaration);
            var filters = GetFilters(context, methodDeclaration);
            
            return new RequestHandlerModel(
                new RequestHandlerNameModel(methodName, "Invoke"),
                controllerType, 
                methodName,
                TypeDefinition.Get("", "InvokeFilter"),
                GetParameters(context, methodDeclaration, cancellation), response, filters);
        }

        private static IReadOnlyList<RequestParameterInformation> GetParameters(
            GeneratorSyntaxContext generatorSyntaxContext, MethodDeclarationSyntax methodDeclaration,
            CancellationToken cancellation)
        {
            var parameters = new List<RequestParameterInformation>();

            foreach (var parameter in methodDeclaration.ParameterList.Parameters)
            {
                RequestParameterInformation? parameterInformation = GetParameterInfoFromAttributes(parameter);

                if (parameterInformation == null)
                {
                    parameterInformation = GetParameterInfo(generatorSyntaxContext, parameter);
                }

                parameters.Add(parameterInformation);
            }

            return parameters;
        }

        private static RequestParameterInformation GetParameterInfo(GeneratorSyntaxContext generatorSyntaxContext,
            ParameterSyntax parameter)
        {
            var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;
            
            return new RequestParameterInformation(
                parameterType,
                parameter.Identifier.Text,
                !parameterType.IsNullable,
                null,
                ParameterBindType.Body,
                    null);
        }

        private static RequestParameterInformation? GetParameterInfoFromAttributes(ParameterSyntax parameter)
        {
            return null;
        }
        
        private static string GetControllerMethod(MethodDeclarationSyntax methodDeclaration)
        {
            return methodDeclaration.Identifier.Text;
        }

        private static ITypeDefinition GetControllerType(SyntaxNode contextNode)
        {
            var classDeclarationSyntax =
                contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(), classDeclarationSyntax.Identifier.Text);
        }


        private static ResponseInformation GetResponseInformation(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration)
        {
            var templateAttribute =
                context.Node.DescendantNodes()
                    .OfType<AttributeSyntax>()
                    .FirstOrDefault(a => a.Name.ToString() == "Template" || a.Name.ToString() == "TemplateAttribute");

            var template = "";

            if (templateAttribute is { ArgumentList.Arguments.Count: > 0 })
            {
                template = templateAttribute.ArgumentList.Arguments[0].ToString().Trim('"');
            }

            var returnType = methodDeclaration.ReturnType.GetTypeDefinition(context);
            
            return new ResponseInformation { TemplateName = template, ReturnType = returnType};
        }
        private static IReadOnlyList<FilterInformationModel> GetFilters(GeneratorSyntaxContext context,
            MethodDeclarationSyntax methodDeclarationSyntax)
        {
            var filterList = new List<FilterInformationModel>();

            filterList.AddRange(GetFiltersForMethod(context, methodDeclarationSyntax));
            filterList.AddRange(GetFiltersForClass(context, methodDeclarationSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()));
            return filterList;
        }

        private static IEnumerable<FilterInformationModel> GetFiltersForClass(GeneratorSyntaxContext context, ClassDeclarationSyntax? parent)
        {
            if (parent == null)
            {
                return Enumerable.Empty<FilterInformationModel>();
            }

            return GetFiltersFromAttributes(context, parent.AttributeLists);
        }

        private static bool IsNotFilterAttribute(AttributeSyntax attribute)
        {
            var attributeName = attribute.Name.ToString().Replace("Attribute", "");

            switch (attributeName)
            {
                case "LambdaFunction":
                case "Template":
                    return false;
            }

            return true;
        }

        private static IEnumerable<FilterInformationModel> GetFiltersForMethod(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclarationSyntax)
        {
            return GetFiltersFromAttributes(context, methodDeclarationSyntax.AttributeLists);
        }

        private static IEnumerable<FilterInformationModel> GetFiltersFromAttributes(GeneratorSyntaxContext context,
            SyntaxList<AttributeListSyntax> attributeListSyntax)
        {
            foreach (var attributeList in attributeListSyntax)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (IsNotFilterAttribute(attribute))
                    {
                        var operation = context.SemanticModel.GetOperation(attribute);

                        if (operation is { Type: { } })
                        {
                            var arguments = "";
                            var propertyAssignment = "";

                            if (attribute.ArgumentList != null)
                            {
                                foreach (var attributeArgumentSyntax in attribute.ArgumentList.Arguments)
                                {
                                    if (attributeArgumentSyntax.ToString().Contains("="))
                                    {
                                        if (propertyAssignment.Length > 0)
                                        {
                                            propertyAssignment += ", ";
                                        }
                                        propertyAssignment += attributeArgumentSyntax.ToString();
                                    }
                                    else
                                    {
                                        if (arguments.Length > 0)
                                        {
                                            arguments += ", ";
                                        }

                                        arguments += attributeArgumentSyntax.ToString();
                                    }
                                }
                            }

                            yield return new FilterInformationModel(operation.Type.GetTypeDefinition(), arguments, propertyAssignment);
                        }
                    }
                }
            }
        }
    }
}
