using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Function;

public class FunctionModelGenerator : BaseRequestModelGenerator {
    private readonly List<string> _attributeNames = new() {
        "HardenedFunction"
    };

    protected override RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation) {
        var attribute =
            methodDeclaration.GetAttribute(
                KnownTypes.Requests.HardenedFunctionAttribute.Name.Replace("Attribute", ""))!;
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();

        var functionName = methodDeclaration.Identifier.Text;

        if (argument != null) {
            var constantValue = context.SemanticModel.GetConstantValue(argument.Expression);

            if (constantValue.HasValue && constantValue.Value != null) {
                functionName = constantValue.Value.ToString();
            } else {
                functionName = argument.Expression.ToString().Trim('"');
            }
        }

        return new RequestHandlerNameModel(functionName, "POST");
    }

    protected override ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation) {
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
        ParameterSyntax parameter, int parameterIndex) {
        foreach (var attributeList in parameter.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                var attributeName = attribute.Name.ToString().Replace("Attribute", "");

                switch (attributeName) {
                    case "FromContext":
                        var headerName =
                            attribute.ArgumentList?.Arguments.FirstOrDefault()?.ToFullString() ?? "";

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Header, headerName, parameterIndex);

                    default:
                        return DefaultGetParameterFromAttribute(
                            attribute, generatorSyntaxContext, parameter, parameterIndex);
                }
            }
        }

        return null;
    }

    protected override bool IsFilterAttribute(AttributeSyntax attribute) {
        var attributeName = attribute.Name.ToString().Replace("Attribute", "");

        switch (attributeName) {
            case "Template":
            case "RawResponse":
            case "HardenedFunction":
                return false;

            default:
                return !_attributeNames.Contains(attributeName);
        }
    }

    private static RequestParameterInformation GetParameterInfoWithBinding(
        GeneratorSyntaxContext generatorSyntaxContext,
        ParameterSyntax parameter,
        ParameterBindType bindingType,
        string bindingName,
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;

        return CreateRequestParameterInformation(parameter,
            parameterType,
            bindingType,
            parameterIndex,
            null,
            bindingName);
    }
}
