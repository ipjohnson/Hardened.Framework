using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Hardened.SourceGenerator.Web;

public class WebRequestHandlerModelGenerator : BaseRequestModelGenerator {
    private static readonly HashSet<string> _attributeNames = GetAttributeNames();

    protected override RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation) {
        var attribute = GetWebAttribute(methodDeclaration, cancellation);

        if (attribute == null) {
            // we should never get here as this check was done in the previous source generator step
            throw new Exception("Could not find attribute");
        }

        var methodName = attribute.Name.ToString().ToUpperInvariant().Replace("Attribute", "");

        if (methodName == "HTTPMETHOD") {
            throw new NotImplementedException("HttpMethodAttribute not supported yet.");
        }

        var pathTemplate = GetPathFromAttribute(context, attribute);

        return new RequestHandlerNameModel(pathTemplate, methodName);
    }

    private static string GetPathFromAttribute(GeneratorSyntaxContext generatorSyntaxContext,
        AttributeSyntax attribute) {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();
        var pathTemplate = "/";
        if (argument != null) {
            var constantValue =
                generatorSyntaxContext.SemanticModel.GetConstantValue(argument.Expression);

            if (constantValue.Value != null) {
                pathTemplate = constantValue.Value.ToString();
            }
            else {
                pathTemplate = argument.Expression.ToString().Trim('"');
            }
        }

        return pathTemplate;
    }

    protected override ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation) {
        var classDeclarationSyntax =
            methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().First();

        var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();

        var className = classDeclarationSyntax.Identifier + "_" + methodDeclaration.Identifier.Text;

        if (methodDeclaration.ParameterList.Parameters.Count > 0) {
            var parameterString = "";

            foreach (var parameter in methodDeclaration.ParameterList.Parameters) {
                parameterString += '|' + parameter.Identifier.Text;
            }

            className += "_" + parameterString.Select(c => (int)c).Aggregate((total, c) => total + c);
        }


        return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd() + ".Generated", className);
    }

    protected override RequestParameterInformation? GetParameterInfoFromAttributes(
        GeneratorSyntaxContext generatorSyntaxContext, MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter,
        int parameterIndex) {
        foreach (var attributeList in parameter.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var attributeName = attribute.Name.ToString().Replace("Attribute", "");

                switch (attributeName) {
                    case "FromHeader":
                        var headerName =
                            attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString() ?? "";

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Header, headerName,parameterIndex);

                    case "FromQueryString":
                        var queryName =
                            attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString() ?? "";

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.QueryString, queryName,parameterIndex);

                    case "FromServices":
                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.FromServiceProvider, "",parameterIndex);

                    case "FromBody":
                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Body, "",parameterIndex);
                    
                    default:
                        return DefaultGetParameterFromAttribute(attribute, generatorSyntaxContext, parameter, parameterIndex);
                }
            }
        }

        return null;
    }

    private RequestParameterInformation GetParameterInfoWithBinding(
        GeneratorSyntaxContext generatorSyntaxContext, ParameterSyntax parameter, ParameterBindType bindingType,
        string bindingName,
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;
        var name = parameter.Identifier.Text;

        string? defaultValue = null;

        if (parameter.Default != null) {
            defaultValue = parameter.Default.Value.ToFullString();
        }

        return new RequestParameterInformation(
            parameterType,
            name,
            !parameterType.IsNullable,
            defaultValue,
            bindingType,
            string.IsNullOrEmpty(bindingName) ? name : bindingName,
            parameterIndex);
    }

    protected override bool IsFilterAttribute(AttributeSyntax attribute) {
        var attributeName = attribute.Name.ToString().Replace("Attribute", "");

        switch (attributeName) {
            case "Template":
            case "RawResponse":
                return false;

            default:
                return !_attributeNames.Contains(attributeName);
        }
    }

    public bool SelectWebRequestMethods(SyntaxNode arg1, CancellationToken arg2) {
        return arg1 is MethodDeclarationSyntax methodDeclarationSyntax &&
               GetWebAttribute(methodDeclarationSyntax, arg2) != null;
    }

    private static AttributeSyntax? GetWebAttribute(MethodDeclarationSyntax node, CancellationToken cancellationToken) {
        var attributeNames =
            node.DescendantNodes().OfType<AttributeSyntax>();

        foreach (var attributeNode in attributeNames) {
            if (cancellationToken.IsCancellationRequested) {
                break;
            }

            var name = attributeNode.Name.ToString();

            if (_attributeNames.Contains(name)) {
                return attributeNode;
            }
        }

        return null;
    }

    private static HashSet<string> GetAttributeNames() {
        var returnSet = new HashSet<string>();
        var names = new List<string> {
            "Get",
            "Put",
            "Post",
            "Patch",
            "Delete",
            "HttpMethod"
        };

        foreach (var name in names) {
            returnSet.Add(name);
            returnSet.Add(name + "Attribute");
        }

        return returnSet;
    }
}