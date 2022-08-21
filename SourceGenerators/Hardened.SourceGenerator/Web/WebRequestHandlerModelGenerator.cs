using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web
{
    public class WebRequestHandlerModelGenerator : BaseRequestModelGenerator
    {
        private static readonly HashSet<string> _attributeNames = GetAttributeNames();

        protected override RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration,
            CancellationToken cancellation)
        {
            var attribute = GetWebAttribute(methodDeclaration, cancellation);

            if (attribute == null)
            {
                // we should never get here as this check was done in the previous source generator step
                throw new Exception("Could not find attribute");
            }

            var methodName = attribute.Name.ToString().ToUpperInvariant().Replace("Attribute","");

            if (methodName == "HTTPMETHOD")
            {
                throw new NotImplementedException("HttpMethodAttribute not supported yet.");
            }

            var pathTemplate = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString().Trim('"') ??
                               "/";

            return new RequestHandlerNameModel(pathTemplate, methodName);
        }

        protected override ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration,
            CancellationToken cancellation)
        {
            var classDeclarationSyntax =
                methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd() + ".Generated", classDeclarationSyntax.Identifier + "_" + methodDeclaration.Identifier.Text);
        }

        protected override RequestParameterInformation? GetParameterInfoFromAttributes(
            GeneratorSyntaxContext generatorSyntaxContext, ParameterSyntax parameter)
        {

            foreach (var attributeList in parameter.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString().Replace("Attribute", "");

                    switch (attributeName)
                    {
                        case "FromHeader":
                            var headerName =
                                attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString() ?? "";

                            return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                                ParameterBindType.Header, headerName);

                        case "FromQueryString":
                            var queryName =
                                attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString() ?? "";

                            return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                                ParameterBindType.Header, queryName);

                        case "FromServices":
                            return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                                ParameterBindType.ServiceProvider, "");

                        case "FromBody":
                            return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                                ParameterBindType.Body, "");
                    }
                }
            }

            return null;
        }

        private RequestParameterInformation GetParameterInfoWithBinding(
            GeneratorSyntaxContext generatorSyntaxContext, ParameterSyntax parameter, ParameterBindType bindingType, string bindingName)
        {
            var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;
            var name = parameter.Identifier.Text;

            return new RequestParameterInformation(
                parameterType,
                name,
                !parameterType.IsNullable,
                null,
                bindingType,
                string.IsNullOrEmpty(bindingName) ? name : bindingName);
        }

        protected override bool IsFilterAttribute(AttributeSyntax attribute)
        {
            var attributeName = attribute.Name.ToString().Replace("Attribute", "");

            switch (attributeName)
            {
                case "Template":
                    return false;

                default:
                    return !_attributeNames.Contains(attributeName);
            }
        }

        public bool SelectWebRequestMethods(SyntaxNode arg1, CancellationToken arg2)
        {
            return arg1 is MethodDeclarationSyntax methodDeclarationSyntax &&
                   GetWebAttribute(methodDeclarationSyntax, arg2) != null;
        }

        private static AttributeSyntax? GetWebAttribute(MethodDeclarationSyntax node, CancellationToken cancellationToken)
        {
            var attributeNames =
                node.DescendantNodes().OfType<AttributeSyntax>();

            foreach (var attributeNode in attributeNames)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var name = attributeNode.Name.ToString();

                if (_attributeNames.Contains(name))
                {
                    return attributeNode;
                }
            }

            return null;
        }

        private static HashSet<string> GetAttributeNames()
        {
            var returnSet = new HashSet<string>();
            var names = new List<string> { "Get", "Put", "Post", "Patch", "Delete", "HttpMethod" };

            foreach (var name in names)
            {
                returnSet.Add(name);
                returnSet.Add(name + "Attribute");
            }

            return returnSet;
        }
    }
}
