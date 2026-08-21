using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

public abstract class BaseRequestModelGenerator {
    public virtual RequestHandlerModel GenerateRequestModel(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        var methodName = GetControllerMethod(methodDeclaration);
        var controllerType = GetControllerType(methodDeclaration);
        var response = GetResponseInformation(context, methodDeclaration);
        var filters = GetFilters(context, methodDeclaration, cancellationToken);

        var nameModel = GetRequestNameModel(context, methodDeclaration, cancellationToken);

        var parameters = GetParameters(context, methodDeclaration, nameModel, cancellationToken);

        return new RequestHandlerModel(
            nameModel,
            controllerType,
            methodName,
            GetInvokeHandlerType(context, methodDeclaration, cancellationToken),
            parameters,
            response,
            filters) {
            ResponseSchema = OpenApiDocument.JsonSchemaWriter.Write(
                SchemaSubject(context, methodDeclaration, response)),
            ResponseSchemas = DeclaredResponses(context, response),
            RequestSchema = BodySchema(context, methodDeclaration, parameters)
        };
    }

    /// <summary>
    /// Every response the handler declares, with the schema of each, or nothing where it declares
    /// one type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the code-first document say what a specification-first one says. Until it
    /// existed the document carried a single hardcoded 200 for every operation, so a handler that
    /// could answer 404 or 409 published a contract claiming it could not - and a client generated
    /// from that document had no branch for either.
    /// </para>
    /// <para>
    /// A case with no schema is a case whose type resolves to nothing this compilation can name,
    /// which should not happen - the type came from the handler's own signature - so the entry is
    /// still written with its status and no content rather than dropped. A response missing from
    /// the document is worse than one whose body is unstated.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ResponseSchemaModel> DeclaredResponses(
        GeneratorSyntaxContext context, ResponseInformationModel response) {
        if (response.UnionCases == null) {
            return Array.Empty<ResponseSchemaModel>();
        }

        var responses = new List<ResponseSchemaModel>();

        foreach (var unionCase in UnionResponseSelector.Decode(response.UnionCases)) {
            var symbol = context.SemanticModel.Compilation.GetTypeByMetadataName(
                unionCase.TypeName.Replace("global::", ""));

            responses.Add(new ResponseSchemaModel(
                unionCase.Status,
                HttpResponseDescription.For(unionCase.Status),
                unionCase.HasBody ? OpenApiDocument.JsonSchemaWriter.Write(symbol) : null));
        }

        return responses;
    }

    /// <summary>
    /// The type the success response's schema is written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declared return type for an ordinary handler, and the <em>success case</em> for one
    /// returning a response set. Writing the wrapper's schema would put a
    /// <c>Response&lt;Todo, NotFound&gt;</c> component into the document whose only member is an
    /// untyped <c>Value</c> - a shape no client can use and no handler ever sends, offered as the
    /// contract for the 200.
    /// </para>
    /// <para>
    /// This is a lockstep site rather than a feature: the document's full response set - the 404 and
    /// the 409 alongside the 200 - is fed through <c>OperationModel</c> separately. What this does
    /// is keep the 200 exactly as correct as it was before response sets existed, rather than
    /// letting a half-done version emit something worse than the thing it replaced.
    /// </para>
    /// </remarks>
    private static ITypeSymbol? SchemaSubject(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        ResponseInformationModel response) {
        var declared = context.SemanticModel.GetTypeInfo(methodDeclaration.ReturnType).Type;

        if (response.UnionCases == null) {
            return declared;
        }

        var successStatus = response.DefaultStatusCode ?? 200;

        var success = UnionResponseSelector.Decode(response.UnionCases)
            .FirstOrDefault(c => c.Status == successStatus);

        return success.TypeName == null
            ? declared
            : context.SemanticModel.Compilation.GetTypeByMetadataName(
                  success.TypeName.Replace("global::", "")) ?? declared;
    }

    /// <summary>
    /// The schema of the parameter bound from the body, if the handler takes one.
    /// </summary>
    private static HandlerSchema? BodySchema(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        IReadOnlyList<RequestParameterInformation> parameters) {
        var body = parameters.FirstOrDefault(p => p.BindingType == ParameterBindType.Body);

        if (body == null) {
            return null;
        }

        var syntax = methodDeclaration.ParameterList.Parameters
            .FirstOrDefault(p => p.Identifier.Text == body.Name);

        return syntax?.Type == null
            ? null
            : OpenApiDocument.JsonSchemaWriter.Write(context.SemanticModel.GetTypeInfo(syntax.Type).Type);
    }

    protected abstract RequestHandlerNameModel GetRequestNameModel(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation);

    protected abstract ITypeDefinition GetInvokeHandlerType(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation);

    protected virtual IReadOnlyList<RequestParameterInformation> GetParameters(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclaration,
        RequestHandlerNameModel requestHandlerNameModel,
        CancellationToken cancellationToken) {
        var parameters = new List<RequestParameterInformation>();
        for(var i = 0; i < methodDeclaration.ParameterList.Parameters.Count; i++) {
            var parameter = methodDeclaration.ParameterList.Parameters[i];
            cancellationToken.ThrowIfCancellationRequested();

            RequestParameterInformation? parameterInformation =
                GetParameterInfoFromAttributes(generatorSyntaxContext, methodDeclaration,
                    requestHandlerNameModel,
                    parameter,
                    i);

            if (parameterInformation == null) {
                parameterInformation = GetParameterInfo(
                    generatorSyntaxContext, 
                    methodDeclaration,
                    requestHandlerNameModel, 
                    parameter,
                    i);
            }

            parameters.Add(parameterInformation);
        }

        return parameters;
    }

    protected virtual RequestParameterInformation? DefaultGetParameterFromAttribute(
        AttributeSyntax attribute, 
        GeneratorSyntaxContext generatorSyntaxContext, 
        ParameterSyntax parameter, 
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
                ParameterBindType.CustomAttribute,
                "",
                parameterIndex,
                AttributeModelHelper.GetAttribute(generatorSyntaxContext, attribute)
                );
    }

    
    protected virtual RequestParameterInformation GetParameterInfo(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter,
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext);

        // Resolution returns null for a name the compiler cannot bind, which happens constantly in
        // an editor - a signature is briefly invalid on the way to being valid, mid-rename or
        // before the model class is written. This used to carry a null forward behind a `!` and
        // dereference a few lines down, which threw out of the syntax transform and cost the whole
        // assembly its generated code, not just this handler. Now the parameter is recorded as
        // unresolved and the handler is skipped at the output stage, where a diagnostic can
        // actually be reported.
        if (parameterType == null) {
            return new RequestParameterInformation(
                TypeDefinition.Get("", parameter.Type?.ToString() ?? "?"),
                parameter.Identifier.Text,
                false,
                null,
                ParameterBindType.Unresolved,
                parameter.Identifier.Text,
                parameterIndex);
        }

        if (KnownTypes.Requests.IExecutionContext.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionContext,
                parameterIndex,
                true);
        }

        if (KnownTypes.Requests.IExecutionRequest.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionRequest,
                parameterIndex,
                true);
        }

        if (KnownTypes.Requests.IExecutionResponse.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionResponse,
                parameterIndex,
                true);
        }

        if (KnownTypes.DI.IServiceProvider.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ServiceProvider,parameterIndex);
        }

        // Ahead of the interface and body branches, because CancellationToken is a struct and would
        // otherwise fall all the way through to Body - deserializing a request body into a
        // CancellationToken, which fails at run time on a signature that reads as ordinary C#.
        if (KnownTypes.System.CancellationToken.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.CancellationToken,
                parameterIndex,
                true);
        }

        if (parameterType.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.FromServiceProvider,parameterIndex);
        }

        var id = parameter.Identifier.Text;

        if (RouteTokens.BindsParameter(requestHandlerNameModel.Path, id)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.Path,parameterIndex);
        }

        return CreateRequestParameterInformation(parameter, parameterType, ParameterBindType.Body,parameterIndex);
    }

    public static RequestParameterInformation CreateRequestParameterInformation(
        ParameterSyntax parameter,
        ITypeDefinition parameterType,
        ParameterBindType parameterBindType,
        int parameterIndex,
        bool? required = null,
        string? bindingName = null,
        AttributeModel? customAttribute = null) {
        if (!parameterType.IsNullable && parameter.ToFullString().Contains("?")) {
            parameterType = parameterType.MakeNullable();
        }

        string? defaultValue = null;

        if (parameter.Default != null) {
            defaultValue = parameter.Default.Value.ToFullString();
        }
        
        return new RequestParameterInformation(
            parameterType,
            parameter.Identifier.Text,
            required ?? !parameterType.IsNullable,
            defaultValue,
            parameterBindType,
            bindingName ?? string.Empty,
            parameterIndex,
            customAttribute);
    }

    protected abstract RequestParameterInformation? GetParameterInfoFromAttributes(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter,
        int parameterIndex);

    protected virtual string GetControllerMethod(MethodDeclarationSyntax methodDeclaration) {
        return methodDeclaration.Identifier.Text;
    }

    protected virtual ITypeDefinition GetControllerType(SyntaxNode contextNode) {
        var classDeclarationSyntax =
            contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

        var namespaceSyntax = classDeclarationSyntax.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>().First();

        return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(),
            classDeclarationSyntax.Identifier.Text);
    }

    protected virtual ResponseInformationModel GetResponseInformation(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration) {
        var output = OutputAttributeSelector.Read(context, methodDeclaration);

        var returnType = methodDeclaration.ReturnType.GetTypeDefinition(context);

        var isAsync = false;
        var isAsyncEnumerable = false;
        ITypeDefinition? asyncEnumerableItemType = null;

        if (returnType is GenericTypeDefinition genericType) {
            if (genericType.Name.Equals("Task") || genericType.Name.Equals("ValueTask")) {
                isAsync = true;
            } else if (genericType.Name.Equals("IAsyncEnumerable")) {
                isAsyncEnumerable = true;
                asyncEnumerableItemType = genericType.TypeArguments[0];
            }
        } else if (returnType?.Name == "Task") {
            isAsync = true;
            returnType = TypeDefinition.Get(typeof(void));
        }

        var rawResponse = "";
        var varResponseAttribute = context.Node.GetAttribute("RawResponse");

        if (varResponseAttribute != null) {
            rawResponse =
                varResponseAttribute.ArgumentList?.Arguments[0].ToString().Trim('"') ??
                "text/plain";
        }

        // Framing is named here and validated where a diagnostic can be reported - a syntax
        // transform cannot report one, so an attribute on a handler that streams nothing has to be
        // carried forward rather than rejected in place.
        var framing = context.Node.GetAttribute("ServerSentEvents") != null
            ? StreamFramingNames.ServerSentEvents
            : null;

        var successStatus = DeclaredSuccessStatus(context);

        return new ResponseInformationModel {
            StreamFraming = framing,
            IsAsync = isAsync,
            IsAsyncEnumerable = isAsyncEnumerable,
            AsyncEnumerableItemType = asyncEnumerableItemType,
            OutputType = output,
            ReturnType = returnType,
            RawResponseContentType = rawResponse,
            DefaultStatusCode = successStatus,
            ProducedContentTypes = DeclaredContentTypes(context),

            // Structural, so this recognises Response<T1..Tn>, a generated response union and a
            // C# 15 union declaration through one check - and returns null for everything else,
            // which is every handler that exists today.
            UnionCases = UnionResponseSelector.Read(context.SemanticModel, methodDeclaration, successStatus),
            UnionDiagnostic =
                UnionResponseSelector.Diagnose(context.SemanticModel, methodDeclaration, successStatus)
        };
    }

    /// <summary>
    /// <c>SuccessStatus</c> from the verb attribute, or null for 200.
    /// </summary>
    /// <remarks>
    /// The hand-written half of what a description states with a <c>responses:</c> key. Both land on
    /// <c>ResponseInformationModel.DefaultStatusCode</c>, which is what keeps the two front ends to
    /// one runtime behaviour rather than two that agree by inspection.
    /// </remarks>
    private static int? DeclaredSuccessStatus(GeneratorSyntaxContext context) {
        foreach (var verb in RoutingVerbs) {
            var attribute = context.Node.GetAttribute(verb);

            if (attribute?.ArgumentList == null) {
                continue;
            }

            foreach (var argument in attribute.ArgumentList.Arguments) {
                if (argument.NameEquals?.Name.Identifier.Text != "SuccessStatus") {
                    continue;
                }

                if (int.TryParse(
                        argument.Expression.ToString(),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var status) &&
                    status != 200) {
                    return status;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <c>[SupportedContentTypes(...)]</c>, comma-joined, or null where the handler said nothing.
    /// </summary>
    /// <remarks>
    /// The hand-written half of a described operation's <c>content:</c> keys. Read from syntax
    /// rather than the semantic model, like everything else here.
    /// </remarks>
    private static string? DeclaredContentTypes(GeneratorSyntaxContext context) {
        var attribute = context.Node.GetAttribute("SupportedContentTypes");

        if (attribute?.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0) {
            return null;
        }

        var types = new List<string>();

        foreach (var argument in attribute.ArgumentList.Arguments) {
            var literal = argument.Expression.ToString().Trim();

            if (literal.Length > 1 && literal[0] == '"' && literal[literal.Length - 1] == '"') {
                types.Add(literal.Substring(1, literal.Length - 2));
            }
        }

        return types.Count == 0 ? null : string.Join(",", types);
    }

    private static readonly string[] RoutingVerbs =
        new[] { "Get", "Post", "Put", "Patch", "Delete" };

    protected virtual IReadOnlyList<AttributeModel> GetFilters(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclarationSyntax,
        CancellationToken cancellationToken) {
        var filterList = new List<AttributeModel>();

        filterList.AddRange(
            GetFiltersForMethod(context, methodDeclarationSyntax, cancellationToken));
        filterList.AddRange(GetFiltersForClass(context,
            methodDeclarationSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault(),
            cancellationToken));

        return filterList;
    }

    protected virtual IEnumerable<AttributeModel> GetFiltersForClass(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax? parent,
        CancellationToken cancellationToken) {
        if (parent == null) {
            return Enumerable.Empty<AttributeModel>();
        }

        return GetFiltersFromAttributes(context, parent.AttributeLists, cancellationToken);
    }

    protected abstract bool IsFilterAttribute(AttributeSyntax attribute);

    protected virtual IEnumerable<AttributeModel> GetFiltersForMethod(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclarationSyntax,
        CancellationToken cancellationToken) {
        return GetFiltersFromAttributes(context, methodDeclarationSyntax.AttributeLists,
            cancellationToken);
    }

    protected virtual IEnumerable<AttributeModel> GetFiltersFromAttributes(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeListSyntax,
        CancellationToken cancellationToken) {

        return AttributeModelHelper.GetAttributes(
            context,
            attributeListSyntax,
            cancellationToken,
            IsFilterAttribute);
    }
}