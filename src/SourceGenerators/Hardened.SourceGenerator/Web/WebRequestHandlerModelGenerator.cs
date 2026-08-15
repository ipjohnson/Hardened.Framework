using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Hardened.SourceGenerator.Web;

public class WebRequestHandlerModelGenerator : BaseRequestModelGenerator {
    private static readonly HashSet<string> _attributeNames = GetAttributeNames();

    public override RequestHandlerModel GenerateRequestModel(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var model = base.GenerateRequestModel(context, cancellationToken);

        var controller = context.Node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        model.Tag = GetTagFromController(context, controller, cancellationToken);

        // What the document has to say about the operation beyond its shape, taken from where a
        // developer has already written it rather than from a second set of attributes.
        var (summary, description) = XmlDocumentation.Read(context.Node);

        model.Summary = summary;
        model.Description = description;

        // An obsolete controller deprecates everything on it: the operations are still served, and
        // that is exactly what `deprecated` means in a document.
        model.IsDeprecated =
            HasObsolete(context.Node as MemberDeclarationSyntax) || HasObsolete(controller);

        return model;
    }

    /// <summary>
    /// Against the member's own attribute lists rather than <c>GetAttribute</c>, which searches
    /// descendants - on a class that reaches every attribute on every member, so one obsolete
    /// method would deprecate the whole controller.
    /// </summary>
    private static bool HasObsolete(MemberDeclarationSyntax? member) =>
        member != null &&
        member.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => {
                var name = attribute.Name.ToString();

                return name.EndsWith("Obsolete") || name.EndsWith("ObsoleteAttribute");
            });

    /// <summary>
    /// The tag the controller declared with <c>[Tag]</c>, or null to let the document generator
    /// derive one from the class name. Read here rather than there because it needs the syntax
    /// tree, which is gone by the time a document is written.
    /// </summary>
    private static string? GetTagFromController(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax? classDeclaration,
        CancellationToken cancellationToken) {
        if (classDeclaration == null) {
            return null;
        }

        var tagAttribute = AttributeModelHelper.GetAttributes(
                context,
                classDeclaration.AttributeLists,
                cancellationToken,
                syntax => syntax.Name.ToString().StartsWith("Tag"))
            .FirstOrDefault();

        var argument = tagAttribute?.Arguments.Split(',').FirstOrDefault()?.Trim().Trim('"');

        return string.IsNullOrEmpty(argument) ? null : argument;
    }

    protected override RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation) {
        var attribute = GetWebAttribute(methodDeclaration, cancellation);

        if (attribute == null) {
            // we should never get here as this check was done in the previous source generator step
            throw new Exception("Could not find attribute");
        }

        var methodName = attribute.Name.ToString().ToUpperInvariant().Replace("Attribute", "");

        var pathTemplate = GetPathFromAttribute(context, attribute, cancellation);

        return new RequestHandlerNameModel(pathTemplate, methodName);
    }

    private static string GetPathFromAttribute(GeneratorSyntaxContext generatorSyntaxContext,
        AttributeSyntax attribute, CancellationToken cancellation) {
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

        var classDeclarationSyntaxes = generatorSyntaxContext.Node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        
        if (classDeclarationSyntaxes != null) {
            var pathAttribute = 
                AttributeModelHelper.GetAttributes(generatorSyntaxContext, classDeclarationSyntaxes.AttributeLists, cancellation, syntax => syntax.Name.ToString().StartsWith("BasePath")).FirstOrDefault();
            
            if (pathAttribute != null) {
                var path = pathAttribute.Arguments.Split(',').FirstOrDefault()?.ToString();

                if (path != null) {
                    path = path.Trim('"');
                    pathTemplate = path + pathTemplate;
                }
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
                // A constraint says something about the value, not about where it comes from.
                // Without this it falls to the default branch below and is emitted as a custom
                // binder, so [StringLength(3)] on a route parameter stops the parameter binding at
                // all rather than merely failing to be validated.
                if (ConstraintAttributeFacts.IsConstraint(generatorSyntaxContext, attribute)) {
                    continue;
                }

                var attributeName = attribute.Name.ToString().Replace("Attribute", "");

                switch (attributeName) {
                    case "FromHeader":
                        var headerName =
                            attribute.GetFirstStringArgumentValue(generatorSyntaxContext);

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Header, headerName,parameterIndex);

                    case "FromCookie":
                        var cookieName =
                            attribute.GetFirstStringArgumentValue(generatorSyntaxContext);

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Cookie, cookieName,parameterIndex);

                    case "FromQueryString":
                        var queryName =
                            attribute.GetFirstStringArgumentValue(generatorSyntaxContext);

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
            "Delete"
        };

        foreach (var name in names) {
            returnSet.Add(name);
            returnSet.Add(name + "Attribute");
        }

        return returnSet;
    }
}