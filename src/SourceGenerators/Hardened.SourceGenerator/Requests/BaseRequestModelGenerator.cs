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

        // Read before Compose, because two things it derives ride on the response model: the
        // declarations naming no status, which the emit step has a context to report, and the
        // validation status a [Throws<RequestValidationError>(422)] states.
        var thrown = ThrownResponseSelector.Read(
            context, methodDeclaration, response, cancellationToken);

        // Narrowed here rather than in the selector, because this is the first place the verb and
        // the response shape are both known - and a declaration on a class reaches only the
        // operations it says it does. [ConditionalGet] on a controller installs on the reads.
        var declared = FilterResponseSelector
            .Read(context, methodDeclaration, cancellationToken)
            .For(nameModel.Method, response.IsAsyncEnumerable);

        var refusals = declared.Refusals;

        var model = Compose(
            nameModel,
            controllerType,
            methodName,
            GetInvokeHandlerType(context, methodDeclaration, cancellationToken),
            parameters,
            response,
            filters,
            OpenApiDocument.JsonSchemaWriter.Write(
                SchemaSubject(context, methodDeclaration, response),
                context.SemanticModel.Compilation.Assembly),
            // Every kind of declaration in one list: what a Response or union return type says,
            // what [Throws<T>] says, and what a guard on the operation can answer instead of the
            // handler. The document writer groups by status and does not care which produced an
            // entry. Filter-declared responses go last, so a status the handler declared itself
            // keeps the shape the handler gave it.
            declared.WithHeaders(
                DeclaredResponses(context, response)
                    .Concat(thrown)
                    .Concat(refusals)
                    .ToList()),
            // Complete unless a declaration named only failures and left the success to the return
            // type. [Throws<T>] is one such, and a guard that can refuse the operation is another:
            // both add a status the handler can be answered with instead of running, and neither
            // says anything about what it answers when it does run.
            response.UnionCases != null || (thrown.Count == 0 && refusals.Count == 0),
            BodySchema(context, methodDeclaration, parameters));

        // After Compose, because it is a fact about the handler rather than an input to
        // assembling it: what the attributes declare for the published document.
        SecurityDeclarationSelector.Apply(context, methodDeclaration, model, cancellationToken);

        // And what Compose set aside. A request has one body, so the bridge keeps the first
        // parameter that fell to it; the rest are remembered here for HRDR009, because a
        // parameter silently dropped from the invocation is a CS7036 in a file nobody wrote.
        model.AdditionalBodyParameters = AdditionalBodyParameters(parameters);

        model.DeclaredTimeout = DeclaredTimeoutSelector.Read(context, methodDeclaration);

        // The headers a filter reads before the handler runs, which nothing in the signature
        // mentions and the document therefore published no parameter for.
        model.DeclaredHeaderParameters = declared.HeaderParameters();

        // And the ones it writes on a status the handler answers by returning a value, which
        // WriteSingleResponse rather than WriteDeclaredResponses publishes.
        // Seeded with what the return type itself declares - a Created<T> carries a Location - so a
        // handler returning one publishes the header a set's case would. DeclaredResponses does the
        // same for a set, off the case type rather than the body's.
        var typeHeaders = DeclaredResponseHeaders(context, model.ResponseInformation);

        // Headers() answers null when it merged nothing in, so the type's own would be dropped on
        // every handler no filter also declares a header for - which is all of them.
        model.SingleResponseHeaders =
            declared.Headers(model.ResponseInformation.DefaultStatusCode ?? 200, typeHeaders)
            ?? (typeHeaders.Count > 0 ? typeHeaders : null);

        model.ParameterEnums = ParameterEnums(context, methodDeclaration);

        return model;
    }

    /// <summary>
    /// The wire vocabulary of every enum bound as a parameter, captured while the symbol is in
    /// hand.
    /// </summary>
    /// <remarks>
    /// <c>EnumVocabularies.Collect</c> walks body schemas, so an enum that appears only as a
    /// query, header or path value had no vocabulary anywhere: the parameter published as a bare
    /// string and no wire converter was emitted for it. An enum that also appears in a body
    /// resolves to the same entry by qualified name, which is what masked this - the fixture's
    /// enum happened to be in a response too.
    /// </remarks>
    private static IReadOnlyList<EnumVocabulary> ParameterEnums(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        List<EnumVocabulary>? found = null;

        foreach (var parameter in methodDeclaration.ParameterList.Parameters) {
            if (parameter.Type == null) {
                continue;
            }

            var symbol = context.SemanticModel.GetTypeInfo(parameter.Type).Type;

            if (symbol is INamedTypeSymbol {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                } nullable) {
                symbol = nullable.TypeArguments[0];
            }

            if (symbol is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumSymbol ||
                !EnumWireNaming.IsOwned(enumSymbol, context.SemanticModel.Compilation.Assembly)) {
                continue;
            }

            var naming = EnumWireNaming.For(enumSymbol, EnumWireNaming.AssemblyDefault(enumSymbol));
            var members = EnumWireNaming.Members(enumSymbol, naming);

            if (members.Count == 0) {
                continue;
            }

            var qualified = "global::" + enumSymbol.ToDisplayString();

            found ??= new List<EnumVocabulary>();

            if (found.All(vocabulary => vocabulary.QualifiedName != qualified)) {
                found.Add(new EnumVocabulary(
                    qualified,
                    enumSymbol.Name,
                    naming,
                    members.Select(pair => new EnumWireValue(pair.Member, pair.Wire)).ToList()));
            }
        }

        return found ?? (IReadOnlyList<EnumVocabulary>)System.Array.Empty<EnumVocabulary>();
    }

    /// <summary>
    /// Assembles the extracted pieces into the model the emitters consume.
    /// </summary>
    /// <remarks>
    /// A seam rather than a constructor call, because the two front-ends built on this assemble
    /// differently. Function handlers build the model directly. Web handlers describe themselves
    /// first and let the shared bridge build it, so that a description and a C# declaration reach
    /// the emitters by one path rather than two - which is the whole of what stops a feature
    /// landing on one and not the other.
    /// </remarks>
    protected virtual RequestHandlerModel Compose(
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
        HandlerSchema? requestSchema) =>
        new(nameModel, controllerType, methodName, invokeHandlerType, parameters, response, filters) {
            ResponseSchema = responseSchema,
            ResponseSchemas = responseSchemas,
            DeclaredResponsesAreComplete = responsesAreComplete,
            RequestSchema = requestSchema
        };

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
            // The body's type where the case wraps one, because that is what reaches the wire. A
            // schema written from NotFound<ApiError> would describe a shape carrying the payload
            // under a member, which no client ever receives.
            var described = unionCase.BodyTypeName ?? unionCase.TypeName;

            var symbol = context.SemanticModel.Compilation.GetTypeByMetadataName(
                described.Replace("global::", ""));

            var model = new ResponseSchemaModel(
                unionCase.Status,
                HttpResponseDescription.For(unionCase.Status),
                unionCase.HasBody
                    ? OpenApiDocument.JsonSchemaWriter.Write(
                        symbol, context.SemanticModel.Compilation.Assembly)
                    : null);

            // The headers the case declares, off the case type rather than the body's - a
            // Created<Todo> sends a Todo and carries a Location, and the Location is the case's.
            if (unionCase.AppliesHeaders &&
                CaseSymbol(context, unionCase.TypeName) is { } caseSymbol) {
                model.Headers = UnionResponseSelector.DeclaredHeaders(caseSymbol);
            }

            responses.Add(model);
        }

        return responses;
    }

    /// <summary>
    /// The headers the return type declares, for a handler that returns one on its own.
    /// </summary>
    /// <remarks>
    /// Off the response type rather than the body's: a <c>Created&lt;Todo&gt;</c> sends a
    /// <c>Todo</c> and carries a <c>Location</c>, and the <c>Location</c> is the wrapper's. The
    /// same read <c>DeclaredResponses</c> makes per case.
    /// </remarks>
    private static IReadOnlyList<Hardened.Generation.Models.ResponseHeaderModel> DeclaredResponseHeaders(
        GeneratorSyntaxContext context, ResponseInformationModel response) {
        var single = UnionResponseSelector.Decode(response.DeclaredResponse).FirstOrDefault();

        if (single.TypeName == null || !single.AppliesHeaders) {
            return System.Array.Empty<Hardened.Generation.Models.ResponseHeaderModel>();
        }

        return CaseSymbol(context, single.TypeName) is { } symbol
            ? UnionResponseSelector.DeclaredHeaders(symbol)
            : System.Array.Empty<Hardened.Generation.Models.ResponseHeaderModel>();
    }

    /// <summary>
    /// The symbol for a case type's definition, from the emitted spelling.
    /// </summary>
    /// <remarks>
    /// The emitted name is a closed generic - <c>Created&lt;TestApp.Todo&gt;</c> - and metadata
    /// lookup wants the definition's arity form. The headers live on the definition's constructor,
    /// so the unbound type is the right one to resolve; a case's header names never depend on its
    /// type arguments.
    /// </remarks>
    private static INamedTypeSymbol? CaseSymbol(GeneratorSyntaxContext context, string typeName) {
        var name = typeName.Replace("global::", "");
        var angle = name.IndexOf('<');

        if (angle >= 0) {
            var arity = 1;
            var depth = 0;

            for (var i = angle + 1; i < name.Length; i++) {
                switch (name[i]) {
                    case '<':
                        depth++;
                        break;

                    case '>':
                        depth--;
                        break;

                    case ',' when depth == 0:
                        arity++;
                        break;
                }
            }

            name = name.Substring(0, angle) + "`" +
                   arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return context.SemanticModel.Compilation.GetTypeByMetadataName(name);
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

        // The invoker's substitution, made here as well: a bare Task is void with a different
        // spelling. Without it the schema writer walked Task itself, and the document published a
        // Task component with its BCL entourage as the operation's 200.
        if (declared is INamedTypeSymbol { Arity: 0, Name: "Task" or "ValueTask" } bare &&
            bare.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks") {
            return null;
        }

        if (response.UnionCases == null) {
            // A response type returned on its own describes what it sends, not itself. A schema
            // written from Created<Todo> describes {value, location, status}, which no client ever
            // receives - the same defect writing Response<T1..Tn>'s own schema would have been.
            var single = UnionResponseSelector.Decode(response.DeclaredResponse).FirstOrDefault();

            if (single.BodyTypeName == null) {
                return declared;
            }

            // Null rather than the wrapper where the body type will not resolve.
            // Compilation.GetTypeByMetadataName answers null for a name it finds in more than one
            // reference, which the forwarded BCL primitives are - so Created<string> lands here.
            // No schema is a gap; the wrapper is a shape no client ever receives, offered as the
            // contract.
            return context.SemanticModel.Compilation.GetTypeByMetadataName(
                single.BodyTypeName.Replace("global::", ""));
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
            : OpenApiDocument.JsonSchemaWriter.Write(
                context.SemanticModel.GetTypeInfo(syntax.Type).Type,
                context.SemanticModel.Compilation.Assembly);
    }

    protected abstract RequestHandlerNameModel GetRequestNameModel(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation);

    /// <summary>The names of every body parameter after the first, in declaration order.</summary>
    private static IReadOnlyList<string> AdditionalBodyParameters(
        IReadOnlyList<RequestParameterInformation> parameters) {
        List<string>? additional = null;
        var seen = false;

        foreach (var parameter in parameters) {
            if (parameter.BindingType != ParameterBindType.Body) {
                continue;
            }

            if (seen) {
                (additional ??= new List<string>()).Add(parameter.Name);
            }

            seen = true;
        }

        return (IReadOnlyList<string>?)additional ?? Array.Empty<string>();
    }

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

            // What the parameter's own constraints say for the document, read here because this
            // is where its symbol is. The validator that enforces them is HandlerValidationFrontEnd's,
            // which reads the same attributes through ValidationModules; this reads only the ones
            // the document can say, so the two are the same statement written twice, as they are
            // for a property.
            if (PublishesFacets(parameterInformation.BindingType) &&
                generatorSyntaxContext.SemanticModel.GetDeclaredSymbol(parameter) is { } symbol) {
                parameterInformation.SchemaFacets = SchemaConstraintWriter.FacetsOf(symbol);
                parameterInformation.RequiredByConstraint = SchemaConstraintWriter.IsRequired(symbol);
            }

            parameters.Add(parameterInformation);
        }

        return parameters;
    }

    /// <summary>
    /// Whether the document describes this parameter with a schema of its own, which is where a
    /// facet can be written. A body has a schema the document builds from its type, and a service
    /// or the context is not the caller's to constrain.
    /// </summary>
    private static bool PublishesFacets(ParameterBindType bindingType) =>
        bindingType is ParameterBindType.Path
            or ParameterBindType.QueryString
            or ParameterBindType.Header
            or ParameterBindType.Cookie;

    protected virtual RequestParameterInformation? DefaultGetParameterFromAttribute(
        AttributeSyntax attribute, 
        GeneratorSyntaxContext generatorSyntaxContext, 
        ParameterSyntax parameter, 
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;
        var name = parameter.Identifier.ValueText;

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
                parameter.Identifier.ValueText,
                false,
                null,
                ParameterBindType.Unresolved,
                parameter.Identifier.ValueText,
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

        // ValueText, not Text: a parameter written `@base` is the route token `base`, and Text
        // carries the escape - so the match against the path failed and the parameter fell
        // through to the body, where HRDR005 reported a token nothing bound.
        var id = parameter.Identifier.ValueText;

        if (RouteTokens.BindsParameter(requestHandlerNameModel.Path, id)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.Path,parameterIndex);
        }

        return CreateRequestParameterInformation(
            parameter, parameterType, ParameterBindType.Body, parameterIndex,
            constructorRequiresServices: ConstructorRequiresServices(generatorSyntaxContext, parameter),
            registeredAsService: RegisteredAsService(generatorSyntaxContext, parameter));
    }

    /// <summary>
    /// The DependencyModules registration attributes, which settle what a concrete class is where
    /// the constructor test cannot: a body model is never registered, and a service registered
    /// this way is one whatever its constructors take.
    /// </summary>
    private static readonly string[] RegistrationAttributes = {
        "SingletonServiceAttribute", "ScopedServiceAttribute", "TransientServiceAttribute"
    };

    private const string RegistrationNamespace = "DependencyModules.Runtime.Attributes";

    /// <summary>
    /// Whether the type is registered as a service by attribute.
    /// </summary>
    /// <remarks>
    /// Asked only of a parameter that has fallen to the body, and answered from the semantic model
    /// because the attribute sits on the type's declaration rather than on the parameter. The
    /// trial's <c>TodoStore</c> - a parameterless class with <c>[SingletonService]</c> - passed
    /// the constructor test above, so it bound from the body, answered 400 on every request, and
    /// reached the published document as a request body on a GET.
    /// </remarks>
    private static bool RegisteredAsService(
        GeneratorSyntaxContext generatorSyntaxContext,
        ParameterSyntax parameter) {
        if (parameter.Type == null) {
            return false;
        }

        if (generatorSyntaxContext.SemanticModel.GetTypeInfo(parameter.Type).Type
            is not INamedTypeSymbol type) {
            return false;
        }

        foreach (var attribute in type.GetAttributes()) {
            var attributeClass = attribute.AttributeClass;

            if (attributeClass == null ||
                Array.IndexOf(RegistrationAttributes, attributeClass.Name) < 0) {
                continue;
            }

            if (attributeClass.ContainingNamespace?.ToDisplayString() == RegistrationNamespace) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type has public constructors and every one of them takes an interface.
    /// </summary>
    /// <remarks>
    /// Asked only of a parameter that has fallen to the body, and answered from the semantic model
    /// because the syntax alone cannot say what a name resolves to. The rule is narrow on purpose:
    /// the deserializer cannot construct an interface, so a type whose every constructor demands
    /// one can never be read from a request body, whatever the author intended. A body model with
    /// a parameterless constructor, and an immutable one whose constructor takes its own data, both
    /// fail the test and are left alone.
    /// </remarks>
    private static bool ConstructorRequiresServices(
        GeneratorSyntaxContext generatorSyntaxContext,
        ParameterSyntax parameter) {
        if (parameter.Type == null) {
            return false;
        }

        if (generatorSyntaxContext.SemanticModel.GetTypeInfo(parameter.Type).Type
            is not INamedTypeSymbol type) {
            return false;
        }

        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsRecord) {
            return false;
        }

        var constructors = 0;

        foreach (var constructor in type.InstanceConstructors) {
            if (constructor.DeclaredAccessibility != Accessibility.Public) {
                continue;
            }

            constructors++;

            var takesService = false;

            foreach (var constructorParameter in constructor.Parameters) {
                if (constructorParameter.Type.TypeKind == TypeKind.Interface) {
                    takesService = true;
                    break;
                }
            }

            if (!takesService) {
                return false;
            }
        }

        return constructors > 0;
    }

    public static RequestParameterInformation CreateRequestParameterInformation(
        ParameterSyntax parameter,
        ITypeDefinition parameterType,
        ParameterBindType parameterBindType,
        int parameterIndex,
        bool? required = null,
        string? bindingName = null,
        AttributeModel? customAttribute = null,
        bool constructorRequiresServices = false,
        bool registeredAsService = false) {
        if (!parameterType.IsNullable && parameter.ToFullString().Contains("?")) {
            parameterType = parameterType.MakeNullable();
        }

        string? defaultValue = null;

        if (parameter.Default != null) {
            defaultValue = parameter.Default.Value.ToFullString();
        }
        
        return new RequestParameterInformation(
            parameterType,
            parameter.Identifier.ValueText,
            required ?? !parameterType.IsNullable,
            defaultValue,
            parameterBindType,
            bindingName ?? string.Empty,
            parameterIndex,
            customAttribute,
            constructorRequiresServices,
            registeredAsService);
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

        var producedContentTypes = DeclaredContentTypes(context, methodDeclaration);
        var writesRawBytes = WritesRawBytes(context, methodDeclaration);

        // Framing is named here and reported where a diagnostic can be - a syntax transform
        // cannot report one, so an attribute on a handler that streams nothing is carried forward
        // as a finding rather than rejected in place. The mismatch is decided here because this is
        // where the return type is known.
        //
        // Read off the declared media type rather than off [ServerSentEvents], which derives from
        // [Produces] and declares exactly that type. The two spellings are one declaration, so
        // [Produces("text/event-stream")] frames a stream as events without the second attribute.
        var framing =
            producedContentTypes != null &&
            producedContentTypes.IndexOf(
                Headers.EventStream, StringComparison.OrdinalIgnoreCase) >= 0
                ? StreamFramingNames.ServerSentEvents
                : null;

        var successStatus = DeclaredSuccessStatus(context);

        // What the return type states about itself, for a handler with no set around it. Read
        // before the status below, because a type's own [HttpStatus] is what the document has to
        // publish - the same precedence a case has inside a set.
        var declaredResponse =
            UnionResponseSelector.ReadDeclared(context.SemanticModel, methodDeclaration, successStatus);

        var declaredCase = UnionResponseSelector.Decode(declaredResponse).FirstOrDefault();

        return new ResponseInformationModel {
            DeclaredResponse = declaredResponse,
            StreamFraming = framing,
            StreamFramingDiagnostic = framing != null && !isAsyncEnumerable ? framing : null,
            IsAsync = isAsync,
            IsAsyncEnumerable = isAsyncEnumerable,
            AsyncEnumerableItemType = asyncEnumerableItemType,
            OutputType = output,
            ReturnType = returnType,
            WritesRawBytes = writesRawBytes,

            // Bytes with nothing to say what they are. An error: no default could be inferred and
            // nothing downstream can supply one.
            MissingContentTypeDiagnostic = writesRawBytes && producedContentTypes == null,

            // A model declared as something no serializer here writes. A warning, because the host
            // may register one - see ContentTypeDiagnostics.
            UnproducibleContentTypeDiagnostic = UnproducibleContentTypes(
                producedContentTypes, context, methodDeclaration, isAsyncEnumerable),
            RawResponseContentType = CommittedContentType(
                producedContentTypes, context, methodDeclaration, isAsyncEnumerable),
            // The type's status where it declares one, so a handler returning Created<T> publishes
            // 201 rather than the 200 nothing asked for.
            DefaultStatusCode = declaredCase.TypeName != null ? declaredCase.Status : successStatus,
            ProducedContentTypes = producedContentTypes,

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
    /// <c>[Produces(...)]</c>, comma-joined, or null where nothing in source said anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hand-written half of a described operation's <c>content:</c> keys. Read from syntax
    /// rather than the semantic model, like everything else here.
    /// </para>
    /// <para>
    /// <b>The operation beats its class, and nothing is combined.</b> Two declarations do not
    /// compose into a third the way two authorization requirements do: the nearest one is the
    /// answer. That is what lets a method on a <c>[Produces("text/csv")]</c> controller answer JSON
    /// without the controller having to say so operation by operation. The assembly and the entry
    /// point are the two rungs below these, and they are resolved as the pipeline is composed,
    /// where an assembly's attributes can be read - see <c>ContentTypeResolver</c>.
    /// </para>
    /// <para>
    /// <c>[RawResponse]</c> derives from <c>[Produces]</c> and is read here under its own name,
    /// because this reads syntax and syntax does not know about a base class.
    /// </para>
    /// </remarks>
    private static string? DeclaredContentTypes(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        return DeclaredOn(methodDeclaration.AttributeLists) ??
               DeclaredOn(methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>()
                   .FirstOrDefault()?.AttributeLists);
    }

    private static string? DeclaredOn(SyntaxList<AttributeListSyntax>? attributeLists) {
        if (attributeLists == null) {
            return null;
        }

        foreach (var attributeList in attributeLists.Value) {
            foreach (var attribute in attributeList.Attributes) {
                var name = attribute.Name.ToString();

                if (name is not ("Produces" or "ProducesAttribute" or
                    "RawResponse" or "RawResponseAttribute" or
                    "ServerSentEvents" or "ServerSentEventsAttribute")) {
                    continue;
                }

                // The two aliases take no arguments and declare a fixed media type, which is the
                // whole of what each of them is. Read from syntax, where a base class is invisible,
                // so the literals are repeated here rather than derived.
                if (name.StartsWith("ServerSentEvents")) {
                    return Headers.EventStream;
                }

                // [RawResponse] with no argument is text/plain, which is the default its
                // constructor states. Every other spelling names its types.
                if (attribute.ArgumentList == null ||
                    attribute.ArgumentList.Arguments.Count == 0) {
                    return name.StartsWith("RawResponse") ? "text/plain" : null;
                }

                var types = new List<string>();

                foreach (var argument in attribute.ArgumentList.Arguments) {
                    var literal = argument.Expression.ToString().Trim();

                    if (literal.Length > 1 && literal[0] == '"' && literal[literal.Length - 1] == '"') {
                        types.Add(literal.Substring(1, literal.Length - 2));
                    }
                }

                if (types.Count > 0) {
                    return string.Join(",", types);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The content type to put on the response before the handler runs, or empty where nothing
    /// should be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for a handler that writes its own bytes and declares exactly one media type. Committing
    /// ahead of the handler is what makes a raw handler that throws answer under the type it
    /// promised, and what lets a handler overwrite it to choose one per request.
    /// </para>
    /// <para>
    /// <b>Not for a handler returning a model.</b> A committed content type takes the response out
    /// of negotiation entirely, which is right for bytes and wrong for a model that a client may
    /// legitimately ask for in another representation. This used to be read off
    /// <c>[RawResponse]</c>, where the two could not be confused because the attribute only went on
    /// raw handlers; <c>[Produces]</c> goes on both, so the return type is what separates them.
    /// </para>
    /// </remarks>
    private static string CommittedContentType(
        string? producedContentTypes,
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        bool isAsyncEnumerable) {
        if (producedContentTypes == null ||
            producedContentTypes.IndexOf(',') >= 0 ||
            isAsyncEnumerable ||
            !ReturnsBytesOrText(context, methodDeclaration)) {
            return "";
        }

        return producedContentTypes;
    }

    /// <summary>
    /// Whether the handler's return value is already what goes on the wire, so no serializer can
    /// structure it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>byte[]</c> and <c>Stream</c>, unwrapped from a <c>Task</c> or <c>ValueTask</c>. Returning
    /// either is the handler saying it controls its own serialization, so the pass-through writer is
    /// bound when the pipeline is composed and the response never reaches a serializer whatever it
    /// declares. Stage 4 reads this; the build diagnostic that requires a declaration on these
    /// handlers reads it too.
    /// </para>
    /// <para>
    /// <b>A <c>string</c> is not one of them.</b> It has a JSON reading as well, a quoted string,
    /// and that is what a handler declaring nothing answers with. It takes the pass-through writer
    /// by declaring a media type instead - see <see cref="ReturnsBytesOrText"/>.
    /// </para>
    /// <para>
    /// Read through the semantic model rather than the type name, so a <c>Stream</c> subclass is one
    /// however it is named and a model called <c>EventStream</c> is not.
    /// </para>
    /// </remarks>
    private static bool WritesRawBytes(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        var returnType = UnwrappedReturnType(context, methodDeclaration);

        if (returnType is IArrayTypeSymbol array) {
            return array.ElementType.SpecialType == SpecialType.System_Byte;
        }

        for (var current = returnType; current != null; current = current.BaseType) {
            if (current.Name == "Stream" && current.ContainingNamespace?.ToDisplayString() == "System.IO") {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="WritesRawBytes"/> and <c>string</c>, which is the set a content type is committed
    /// ahead of the handler for.
    /// </summary>
    /// <remarks>
    /// Exactly the return types <c>[RawResponse]</c> could be written on, which is what keeps the
    /// committed-content-type behaviour identical for every handler that carried it. A handler
    /// returning a model never commits: committing takes the response out of negotiation, which is
    /// right for bytes and wrong for a model a client may legitimately ask for another way.
    /// </remarks>
    private static bool ReturnsBytesOrText(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        return WritesRawBytes(context, methodDeclaration) ||
               UnwrappedReturnType(context, methodDeclaration)?.SpecialType == SpecialType.System_String;
    }

    /// <summary>
    /// The declared media types that nothing visible here can write, comma-joined, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decidable at build for exactly two reasons. A handler returning <c>string</c>,
    /// <c>byte[]</c> or <c>Stream</c> writes its own bytes, so every media type it declares is
    /// producible whatever is registered. And <c>application/json</c> is always producible, because
    /// the framework registers a serializer for it and an application replacing that one replaces it
    /// with another that declares the same media type.
    /// </para>
    /// <para>
    /// Everything else is a model declared as something this compilation has no writer for. It may
    /// still be right - the host registers the serializer - which is why it is a warning rather
    /// than an error.
    /// </para>
    /// <para>
    /// A streamed handler is skipped: its media types are the framing's, and the streaming writer
    /// produces both of them.
    /// </para>
    /// </remarks>
    private static string? UnproducibleContentTypes(
        string? producedContentTypes,
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        bool isAsyncEnumerable) {
        if (producedContentTypes == null ||
            isAsyncEnumerable ||
            ReturnsBytesOrText(context, methodDeclaration)) {
            return null;
        }

        var unproducible = new List<string>();

        foreach (var contentType in producedContentTypes.Split(',')) {
            var trimmed = contentType.Trim();

            if (trimmed.Length > 0 &&
                !trimmed.Equals("application/json", StringComparison.OrdinalIgnoreCase)) {
                unproducible.Add(trimmed);
            }
        }

        return unproducible.Count == 0 ? null : string.Join(",", unproducible);
    }

    private static ITypeSymbol? UnwrappedReturnType(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {
        var returnType = context.SemanticModel.GetTypeInfo(methodDeclaration.ReturnType).Type;

        if (returnType is INamedTypeSymbol { IsGenericType: true } generic &&
            generic.Name is "Task" or "ValueTask" &&
            generic.TypeArguments.Length == 1) {
            return generic.TypeArguments[0];
        }

        return returnType;
    }

    private static readonly string[] RoutingVerbs =
        new[] { "Get", "Post", "Put", "Patch", "Delete" };

    /// <summary>
    /// Media type literals the transform compares against, spelled here because a generator cannot
    /// reference the runtime assembly that declares them.
    /// </summary>
    private static class Headers {
        public const string EventStream = "text/event-stream";
    }

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