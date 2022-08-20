using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests
{
    public abstract class BaseRequestModelGenerator
    {
        public virtual RequestHandlerModel GenerateRequestModel(GeneratorSyntaxContext context, CancellationToken cancellation)
        {
            var methodDeclaration = (MethodDeclarationSyntax)context.Node;

            var methodName = GetControllerMethod(methodDeclaration);
            var controllerType = GetControllerType(methodDeclaration);
            var response = GetResponseInformation(context, methodDeclaration);
            var filters = GetFilters(context, methodDeclaration);

            return new RequestHandlerModel(
                GetRequestNameModel(context, methodDeclaration, cancellation),
                controllerType,
                methodName,
                GetInvokeHandlerType(context, methodDeclaration, cancellation),
                GetParameters(context, methodDeclaration, cancellation), response, filters);
        }

        protected abstract RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation);

        protected abstract ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation);
        
        protected virtual IReadOnlyList<RequestParameterInformation> GetParameters(
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

        protected virtual RequestParameterInformation GetParameterInfo(GeneratorSyntaxContext generatorSyntaxContext,
            ParameterSyntax parameter)
        {
            var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;

            return new RequestParameterInformation(
                parameterType,
                parameter.Identifier.Text,
                !parameterType.IsNullable,
                null,
                ParameterBindType.Body,
                    string.Empty);
        }

        protected virtual RequestParameterInformation? GetParameterInfoFromAttributes(ParameterSyntax parameter)
        {
            return null;
        }

        protected virtual string GetControllerMethod(MethodDeclarationSyntax methodDeclaration)
        {
            return methodDeclaration.Identifier.Text;
        }

        protected virtual ITypeDefinition GetControllerType(SyntaxNode contextNode)
        {
            var classDeclarationSyntax =
                contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(), classDeclarationSyntax.Identifier.Text);
        }
        
        protected virtual ResponseInformationModel GetResponseInformation(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration)
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

            return new ResponseInformationModel { TemplateName = template, ReturnType = returnType };
        }
        protected virtual IReadOnlyList<FilterInformationModel> GetFilters(GeneratorSyntaxContext context,
            MethodDeclarationSyntax methodDeclarationSyntax)
        {
            var filterList = new List<FilterInformationModel>();

            filterList.AddRange(GetFiltersForMethod(context, methodDeclarationSyntax));
            filterList.AddRange(GetFiltersForClass(context, methodDeclarationSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()));

            return filterList;
        }

        protected virtual IEnumerable<FilterInformationModel> GetFiltersForClass(GeneratorSyntaxContext context, ClassDeclarationSyntax? parent)
        {
            if (parent == null)
            {
                return Enumerable.Empty<FilterInformationModel>();
            }

            return GetFiltersFromAttributes(context, parent.AttributeLists);
        }

        protected abstract bool IsFilterAttribute(AttributeSyntax attribute);

        protected virtual IEnumerable<FilterInformationModel> GetFiltersForMethod(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclarationSyntax)
        {
            return GetFiltersFromAttributes(context, methodDeclarationSyntax.AttributeLists);
        }

        protected virtual IEnumerable<FilterInformationModel> GetFiltersFromAttributes(GeneratorSyntaxContext context,
            SyntaxList<AttributeListSyntax> attributeListSyntax)
        {
            foreach (var attributeList in attributeListSyntax)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (IsFilterAttribute(attribute))
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
