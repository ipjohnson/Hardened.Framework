using CSharpAuthor;
using Hardened.Generation.Models;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Validation;
using Hardened.SourceGenerator.Web.Routing;
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

        // <param name="x"> is where a developer has already said what a parameter means.
        foreach (var parameter in model.RequestParameterInformationList) {
            parameter.Description = XmlDocumentation.ReadParameter(context.Node, parameter.Name);
        }

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
                    pathTemplate = RoutePath.Combine(path, pathTemplate);
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
                // Some attributes say something other than where the value comes from - a
                // constraint describes the value, [EnumeratorCancellation] is compiler machinery.
                // Without this they fall to the default branch below and are emitted as custom
                // binders, which takes the parameter out of the binding path it was written for.
                if (NonBindingAttributeFacts.IsNonBinding(generatorSyntaxContext, attribute)) {
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

                    case "FromForm":
                        var formName =
                            attribute.GetFirstStringArgumentValue(generatorSyntaxContext);

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Form, formName, parameterIndex);

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

    /// <remarks>
    /// <c>Output</c> is no longer excluded. It used to be, as <c>Template</c>, because the attribute was an
    /// annotation read as response information and putting it in the metadata array as well would
    /// have been a second copy of the same fact. <c>[Template&lt;T&gt;]</c> is a generic attribute,
    /// so its name here spells as <c>Output&lt;Views.Fortunes&gt;</c> and never matched the case
    /// anyway - and a template a filter can see is useful rather than duplicated.
    /// </remarks>
    protected override bool IsFilterAttribute(AttributeSyntax attribute) {
        var attributeName = attribute.Name.ToString().Replace("Attribute", "");

        // A generic attribute's name carries its arguments - "Throws<RateLimited>" - so the
        // exclusions below have to be matched against the bare name. Anything not recognised is
        // taken for a filter and constructed into the handler's filter chain, which is not
        // somewhere a response declaration belongs.
        var generic = attributeName.IndexOf('<');

        if (generic >= 0) {
            attributeName = attributeName.Substring(0, generic);
        }

        switch (attributeName) {
            case "RawResponse":
            case "Throws":
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

    /// <summary>
    /// Describes the handler, then lets the shared bridge build the model from that description.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the point of the pipeline unification. An attribute-routed handler and an operation
    /// in a description now reach the emitters by one path: both become an <c>OperationModel</c>
    /// with its symbols, and <see cref="SpecHandlerModelBuilder"/> turns either into the model
    /// everything downstream consumes. A feature added below this line arrives on all of them.
    /// </para>
    /// <para>
    /// What the description cannot state travels in <see cref="OperationSymbols"/> and nowhere else:
    /// the resolved types, how each parameter binds and under what name, the body parameter's name,
    /// declaration order, defaults already written as C#, custom binding attributes, and the
    /// response shape read off the return type. Each of those was found by putting this path into
    /// the live pipeline and watching an existing test fail.
    /// </para>
    /// </remarks>
    protected override RequestHandlerModel Compose(
        RequestHandlerNameModel nameModel,
        ITypeDefinition controllerType,
        string methodName,
        ITypeDefinition invokeHandlerType,
        IReadOnlyList<RequestParameterInformation> parameters,
        ResponseInformationModel response,
        IReadOnlyList<AttributeModel> filters,
        HandlerSchema? responseSchema,
        IReadOnlyList<ResponseSchemaModel> responseSchemas,
        bool responsesAreComplete,
        HandlerSchema? requestSchema) {
        var operationId = controllerType.Name + "." + methodName + "." + nameModel.Method;

        var body = parameters.FirstOrDefault(p => p.BindingType == ParameterBindType.Body);
        var described = parameters.Where(p => p.BindingType != ParameterBindType.Body).ToList();

        string Wire(RequestParameterInformation p) =>
            string.IsNullOrEmpty(p.BindingName) ? p.Name : p.BindingName;

        var spec = new ServiceSpecModel();

        spec.Services.Add(new ServiceModel {
            Tag = controllerType.Name,
            TypeBaseName = controllerType.Name,
            DispatchHeader = nameModel.DispatchHeader,
            Operations = {
                new OperationModel {
                    OperationId = operationId,
                    MethodName = methodName,
                    Path = nameModel.Path,
                    HttpMethod = nameModel.Method,
                    DispatchKey = nameModel.DispatchKey,
                    Parameters = described.Select(p => new ParameterModel {
                        Name = Wire(p),
                        MemberNameOverride = p.Name,
                        In = Location(p.BindingType),
                        IsRequired = p.Required,
                        // What the parameter's own constraints say for the document, read off
                        // its symbol before this description was written and carried through
                        // it, since the builder below rebuilds every parameter from here.
                        SchemaFacets = p.SchemaFacets,
                        RequiredByConstraint = p.RequiredByConstraint
                    }).ToList()
                }
            }
        });

        var symbols = new Dictionary<string, OperationSymbols> {
            [operationId] = new() {
                ControllerType = controllerType,
                InvokeHandlerType = invokeHandlerType,
                ResponseInformation = response,
                RequestBodyType = body?.ParameterType,
                RequestBodyName = body?.Name,
                RequestBodyRequiresServices = body?.ConstructorRequiresServices ?? false,
                ParameterOrder = parameters.OrderBy(p => p.ParameterIndex).Select(Wire).ToList(),
                ParameterTypes = described.ToDictionary(Wire, p => p.ParameterType, StringComparer.Ordinal),
                ParameterBindings = described.ToDictionary(Wire, p => p.BindingType, StringComparer.Ordinal),
                ParameterDefaults = described.Where(p => p.DefaultValue != null)
                    .ToDictionary(Wire, p => p.DefaultValue!, StringComparer.Ordinal),
                ParameterAttributes = described.Where(p => p.CustomAttribute != null)
                    .ToDictionary(Wire, p => p.CustomAttribute!, StringComparer.Ordinal)
            }
        };

        var built = SpecHandlerModelBuilder.BuildModels(
            spec,
            controllerType.Namespace,
            controllerType.Namespace,
            invokeHandlerType.Namespace,
            controllerType.Namespace,
            symbols);

        var model = built.Count == 1 ? built[0] : null;

        if (model == null) {
            return base.Compose(nameModel, controllerType, methodName, invokeHandlerType,
                parameters, response, filters, responseSchema, responseSchemas,
                responsesAreComplete, requestSchema);
        }

        // Schemas are read from the compilation and have no description to come from. Filters are
        // C# attributes the author wrote.
        model.ResponseSchema = responseSchema;
        model.ResponseSchemas = responseSchemas;
        model.DeclaredResponsesAreComplete = responsesAreComplete;
        model.RequestSchema = requestSchema;

        return filters.Count == 0 ? model : model.WithFilters(filters);
    }

    /// <summary>Where a parameter lives, for the bindings a description has a word for.</summary>
    private static string Location(ParameterBindType bindType) =>
        bindType switch {
            ParameterBindType.Path => "path",
            ParameterBindType.QueryString => "query",
            ParameterBindType.Header => "header",
            ParameterBindType.Cookie => "cookie",
            _ => "internal"
        };
}
