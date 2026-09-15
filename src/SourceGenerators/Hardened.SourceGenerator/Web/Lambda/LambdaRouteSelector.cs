using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Lambda;

/// <summary>
/// Reads a route registered with a lambda.
/// </summary>
/// <remarks>
/// <para>
/// The call site is all there is to read. <c>routes.Get($"/{tenant}/orders/{{id:int}}", (int id,
/// IOrderService orders) =&gt; ...)</c> gives the verb, the parameters, the return type and any
/// attribute written on the lambda - everything a handler is generated from except the path, and
/// the path is the one part that can be substituted at run time.
/// </para>
/// <para>
/// <b>The parameters are classified by type rather than against a template</b>, because there is no
/// template to read. A type that can be read from a string is a path token, matched by name when
/// the route registers; anything else is the body. See <see cref="StringConvertibleFacts"/>.
/// </para>
/// </remarks>
public static class LambdaRouteSelector
{
    private const string RegistryName = "IRouteRegistry";

    private const string RegistryNamespace = "Hardened.Web.Runtime.Routing";

    /// <summary>The verb each method registers.</summary>
    private static readonly Dictionary<string, string> Verbs = new(StringComparer.Ordinal)
    {
        { "Get", "GET" },
        { "Post", "POST" },
        { "Put", "PUT" },
        { "Patch", "PATCH" },
        { "Delete", "DELETE" },
    };

    /// <summary>The method that names its verb in its first argument.</summary>
    public const string MapName = "Map";

    /// <summary>
    /// Whether this looks like a registration with a lambda, on names alone.
    /// </summary>
    /// <remarks>
    /// Deliberately loose. The transform binds and decides; what this has to be is cheap, because
    /// it runs on every invocation in the compilation.
    /// </remarks>
    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) =>
        node
            is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax member,
                ArgumentList.Arguments: var arguments,
            }
        && Named(member, arguments.Count)
        && arguments[arguments.Count - 1].Expression is AnonymousFunctionExpressionSyntax;

    /// <summary>Whether the name and the argument count are a registration's.</summary>
    public static bool Named(MemberAccessExpressionSyntax member, int arguments)
    {
        var name = member.Name.Identifier.ValueText;

        return (arguments == 2 && Verbs.ContainsKey(name)) || (arguments == 3 && name == MapName);
    }

    public static LambdaRouteModel? Transform(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var member = (MemberAccessExpressionSyntax)invocation.Expression;

        if (
            context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol
                is not IMethodSymbol registration
            || registration.ContainingType?.Name != RegistryName
            || registration.ContainingType.ContainingNamespace?.ToDisplayString()
                != RegistryNamespace
        )
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;

        var lambdaSyntax = (AnonymousFunctionExpressionSyntax)
            arguments[arguments.Count - 1].Expression;

        if (
            context.SemanticModel.GetSymbolInfo(lambdaSyntax, cancellationToken).Symbol
            is not IMethodSymbol lambda
        )
        {
            return null;
        }

        // A lambda with no parameter list has no parameters to bind and no names to read. It is
        // still a handler, so it is not refused here.
        var parameterList = (lambdaSyntax as ParenthesizedLambdaExpressionSyntax)?.ParameterList;

        var location = Intercepts(context.SemanticModel, invocation, cancellationToken);

        if (location == null)
        {
            return null;
        }

        var verb = Verb(context, member, arguments, cancellationToken);

        if (verb == null)
        {
            // Map given a verb the build cannot read. LambdaRouteDiagnostics reports it; emitting
            // nothing leaves the declared method to throw, which says the same thing later.
            return null;
        }

        var delegateType = DelegateType(lambda);
        var parameters = Parameters(context, parameterList, cancellationToken);
        var response = Response(context, lambdaSyntax, lambda);

        var handler = new RequestHandlerModel(
            // The tokens this handler reads and the constraint each needs, which is what the
            // document writer reads a declared path for. The registry never reads it - it always
            // builds the route with the template it registered, and WithPath replaces this.
            new RequestHandlerNameModel(RequiredTemplate(parameters), verb),
            delegateType,
            "Invoke",
            TypeDefinition.Get(HandlerNamespace(context), HandlerName(invocation, verb)),
            parameters,
            response,
            Filters(context, lambdaSyntax, cancellationToken)
        )
        {
            IsDelegateHandler = true,

            // What the lambda answers with and what it reads, on the same terms a declared
            // handler's are read. Left unset, the document wrote a 200 with no content and no
            // requestBody at all, so every registered operation published exactly the half a
            // client cannot be generated from.
            ResponseSchema = JsonSchemaWriter.Write(
                BaseRequestModelGenerator.SchemaSubject(
                    context.SemanticModel.Compilation,
                    lambda.ReturnType,
                    response
                ),
                context.SemanticModel.Compilation.Assembly
            ),
            RequestSchema = BodySchema(context, parameterList, parameters),

            // The class the registration is written in, which is the group a reader of the
            // document is looking for. Left unset, the tag came from ControllerType, which for a
            // lambda is its delegate type - so every registered operation in the application
            // documented under "Func".
            Tag = RegistrationGroup(invocation),
        };

        return new LambdaRouteModel(
            verb,
            location,
            delegateType,
            handler,
            BoundTokens(parameters),
            member.Name.Identifier.ValueText == MapName
        );
    }

    /// <summary>
    /// The verb the call registers, or null where <c>Map</c> was given one the build cannot read.
    /// </summary>
    /// <remarks>
    /// A verb has to be known at build time: it is written into the handler's own information and
    /// into the table the route joins. <c>Map(variable, path, lambda)</c> is therefore not a
    /// registration this can emit for, and is reported rather than ignored.
    /// </remarks>
    public static string? Verb(
        GeneratorSyntaxContext context,
        MemberAccessExpressionSyntax member,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        CancellationToken cancellationToken
    )
    {
        if (Verbs.TryGetValue(member.Name.Identifier.ValueText, out var verb))
        {
            return verb;
        }

        var declared = context.SemanticModel.GetConstantValue(
            arguments[0].Expression,
            cancellationToken
        );

        return declared is { HasValue: true, Value: string written } && written.Length > 0
            ? written.ToUpperInvariant()
            : null;
    }

    /// <summary>
    /// The attribute the compiler writes for this call site, or null where it declines to name one.
    /// </summary>
    /// <remarks>
    /// Experimental in Roslyn and suppressed deliberately. The alternative is the older
    /// three-argument spelling, keyed by a file path and a character offset, which every edit above
    /// a registration moved.
    /// </remarks>
    private static string? Intercepts(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken
    )
    {
#pragma warning disable RSEXPERIMENTAL002
        return model
            .GetInterceptableLocation(invocation, cancellationToken)
            ?.GetInterceptsLocationAttributeSyntax();
#pragma warning restore RSEXPERIMENTAL002
    }

    /// <summary>The lambda's natural type - what the compiler gave it at the call site.</summary>
    private static ITypeDefinition DelegateType(IMethodSymbol lambda)
    {
        var arguments = lambda.Parameters.Select(parameter => AsWritten(parameter.Type)).ToList();

        if (lambda.ReturnsVoid)
        {
            return arguments.Count == 0
                ? TypeDefinition.Get("System", "Action")
                : new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    "System",
                    "Action",
                    arguments
                );
        }

        arguments.Add(AsWritten(lambda.ReturnType));

        return new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            "System",
            "Func",
            arguments
        );
    }

    /// <summary>
    /// The type as the delegate declares it, nullability included.
    /// </summary>
    /// <remarks>
    /// <c>GetTypeDefinitionFromType</c> unwraps <c>Nullable&lt;T&gt;</c> to <c>T</c>, which is right
    /// for a bound parameter and wrong for the delegate the lambda was given: a lambda taking
    /// <c>int?</c> has the natural type <c>Func&lt;int?, …&gt;</c>, and a cast written against
    /// <c>Func&lt;int, …&gt;</c> does not compile.
    /// </remarks>
    private static ITypeDefinition AsWritten(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable
            ? TypeSyntaxExtensions
                .GetTypeDefinitionFromType(nullable.TypeArguments[0])
                .MakeNullable()
            : TypeSyntaxExtensions.GetTypeDefinitionFromType(type);

    /// <summary>
    /// What the lambda answers with: its return type, and what it declared about the response.
    /// </summary>
    /// <remarks>
    /// The declarations reached the handler's metadata array and nothing else, so
    /// <c>[Produces]</c> on a lambda changed neither negotiation, nor the document, nor the cache
    /// key, and <c>[Output&lt;T&gt;]</c> was ignored and the model serialized in the view's place -
    /// which is the disclosure the attribute exists to prevent. They are read here, off the rungs a
    /// controller's are read off and into the fields a controller's are written to, so one handler
    /// written two ways answers one way.
    /// </remarks>
    private static ResponseInformationModel Response(
        GeneratorSyntaxContext context,
        AnonymousFunctionExpressionSyntax lambdaSyntax,
        IMethodSymbol lambda
    )
    {
        var response = FromReturnType(lambda);

        var attributeLists =
            (lambdaSyntax as ParenthesizedLambdaExpressionSyntax)?.AttributeLists ?? default;

        var produced = BaseRequestModelGenerator.DeclaredContentTypes(
            context,
            attributeLists,
            lambdaSyntax
        );

        var answered = BaseRequestModelGenerator.AwaitedType(lambda.ReturnType);
        var writesRawBytes = BaseRequestModelGenerator.IsRawPayload(answered);

        var returnsBytesOrText =
            writesRawBytes || answered?.SpecialType == SpecialType.System_String;

        response.OutputType = OutputAttributeSelector.Read(context, attributeLists);
        response.ProducedContentTypes = produced;
        response.WritesRawBytes = writesRawBytes;
        response.ReturnsBytesOrText = returnsBytesOrText;
        response.StreamFraming = BaseRequestModelGenerator.StreamFraming(produced);
        response.RawResponseContentType = BaseRequestModelGenerator.CommittedContentType(
            produced,
            returnsBytesOrText,
            response.IsAsyncEnumerable
        );

        return response;
    }

    private static ResponseInformationModel FromReturnType(IMethodSymbol lambda)
    {
        if (lambda.ReturnsVoid)
        {
            return new ResponseInformationModel { ReturnType = TypeDefinition.Get(typeof(void)) };
        }

        var returnType = AsWritten(lambda.ReturnType);

        if (returnType is GenericTypeDefinition generic)
        {
            if (generic.Name is "Task" or "ValueTask")
            {
                return new ResponseInformationModel { ReturnType = returnType, IsAsync = true };
            }

            if (generic.Name == "IAsyncEnumerable")
            {
                return new ResponseInformationModel
                {
                    ReturnType = returnType,
                    IsAsyncEnumerable = true,
                    AsyncEnumerableItemType = generic.TypeArguments[0],
                };
            }
        }

        if (returnType.Name == "Task" || returnType.Name == "ValueTask")
        {
            return new ResponseInformationModel
            {
                ReturnType = TypeDefinition.Get(typeof(void)),
                IsAsync = true,
            };
        }

        return new ResponseInformationModel { ReturnType = returnType };
    }

    /// <remarks>
    /// C# has allowed attributes on a lambda since version 10, so <c>[Compress]</c>,
    /// <c>[CacheResponse]</c> and <c>[RequireAuthorization]</c> attach here and a registered route
    /// keeps the declarative surface a controller method has.
    /// </remarks>
    private static IReadOnlyList<AttributeModel> Filters(
        GeneratorSyntaxContext context,
        AnonymousFunctionExpressionSyntax lambda,
        CancellationToken cancellationToken
    )
    {
        if (
            lambda is not ParenthesizedLambdaExpressionSyntax { AttributeLists.Count: > 0 } declared
        )
        {
            return Array.Empty<AttributeModel>();
        }

        var attributes = new List<AttributeModel>();

        foreach (var list in declared.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (AttributeModelHelper.GetAttribute(context, attribute) is { } model)
                {
                    attributes.Add(model);
                }
            }
        }

        return attributes;
    }

    /// <summary>
    /// The group a registered operation documents under: the type the registration is written in,
    /// with a <c>Routes</c> suffix removed the way <c>HandlerGroup</c> removes <c>Controller</c>.
    /// </summary>
    /// <remarks>
    /// Null where the call is not inside a type declaration, which leaves the tag to
    /// <c>HandlerGroup</c> and its reading of the delegate type.
    /// </remarks>
    private static string? RegistrationGroup(InvocationExpressionSyntax invocation)
    {
        const string suffix = "Routes";

        var declaring = invocation
            .Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.ValueText;

        if (string.IsNullOrEmpty(declaring))
        {
            return null;
        }

        return
            declaring!.Length > suffix.Length
            && declaring.EndsWith(suffix, StringComparison.Ordinal)
            ? declaring.Substring(0, declaring.Length - suffix.Length)
            : declaring;
    }

    /// <summary>
    /// The schema of the parameter the lambda reads its body from, if it takes one.
    /// </summary>
    /// <remarks>
    /// The same rule the declared form uses, against the lambda's parameter list: a request
    /// carries one body, so the first parameter that fell to it is the one described.
    /// </remarks>
    private static HandlerSchema? BodySchema(
        GeneratorSyntaxContext context,
        ParameterListSyntax? parameterList,
        IReadOnlyList<RequestParameterInformation> parameters
    )
    {
        var body = parameters.FirstOrDefault(parameter =>
            parameter.BindingType == ParameterBindType.Body
        );

        if (body == null || parameterList == null)
        {
            return null;
        }

        var syntax = parameterList.Parameters.FirstOrDefault(parameter =>
            parameter.Identifier.Text == body.Name
        );

        return syntax?.Type == null
            ? null
            : JsonSchemaWriter.Write(
                context.SemanticModel.GetTypeInfo(syntax.Type).Type,
                context.SemanticModel.Compilation.Assembly
            );
    }

    private static IReadOnlyList<RequestParameterInformation> Parameters(
        GeneratorSyntaxContext context,
        ParameterListSyntax? parameterList,
        CancellationToken cancellationToken
    )
    {
        if (parameterList == null)
        {
            return Array.Empty<RequestParameterInformation>();
        }

        var parameters = new List<RequestParameterInformation>();

        for (var index = 0; index < parameterList.Parameters.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            parameters.Add(Parameter(context, parameterList.Parameters[index], index));
        }

        return parameters;
    }

    /// <remarks>
    /// The same order a handler method's parameters are read in, with one difference at the end:
    /// where an attribute route asks whether the name is in the template, this asks whether the
    /// type can be read from a string.
    /// </remarks>
    private static RequestParameterInformation Parameter(
        GeneratorSyntaxContext context,
        ParameterSyntax parameter,
        int index
    )
    {
        var type = parameter.Type?.GetTypeDefinition(context);

        if (type == null)
        {
            return new RequestParameterInformation(
                TypeDefinition.Get("", parameter.Type?.ToString() ?? "?"),
                parameter.Identifier.ValueText,
                false,
                null,
                ParameterBindType.Unresolved,
                parameter.Identifier.ValueText,
                index
            );
        }

        var special = SpecialBinding(type);

        if (special != null)
        {
            return BaseRequestModelGenerator.CreateRequestParameterInformation(
                parameter,
                type,
                special.Value,
                index,
                true
            );
        }

        if (type.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition)
        {
            return BaseRequestModelGenerator.CreateRequestParameterInformation(
                parameter,
                type,
                ParameterBindType.FromServiceProvider,
                index
            );
        }

        var symbol =
            parameter.Type == null ? null : context.SemanticModel.GetTypeInfo(parameter.Type).Type;

        if (StringConvertibleFacts.IsStringConvertible(type, symbol))
        {
            return BaseRequestModelGenerator.CreateRequestParameterInformation(
                parameter,
                type,
                ParameterBindType.Path,
                index,
                bindingName: parameter.Identifier.ValueText
            );
        }

        return BaseRequestModelGenerator.CreateRequestParameterInformation(
            parameter,
            type,
            ParameterBindType.Body,
            index
        );
    }

    private static ParameterBindType? SpecialBinding(ITypeDefinition type)
    {
        if (KnownTypes.Requests.IExecutionContext.Equals(type))
        {
            return ParameterBindType.ExecutionContext;
        }

        if (KnownTypes.Requests.IExecutionRequest.Equals(type))
        {
            return ParameterBindType.ExecutionRequest;
        }

        if (KnownTypes.Requests.IExecutionResponse.Equals(type))
        {
            return ParameterBindType.ExecutionResponse;
        }

        if (KnownTypes.DI.IServiceProvider.Equals(type))
        {
            return ParameterBindType.ServiceProvider;
        }

        if (KnownTypes.System.CancellationToken.Equals(type))
        {
            return ParameterBindType.CancellationToken;
        }

        return null;
    }

    /// <summary>
    /// The path tokens the emitted binder reads, each with the constraint names that would let it
    /// read them without being able to refuse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>id</c> where the parameter is a string, which binds as itself; <c>id:int|range</c> where
    /// it is an <c>int</c>, which a value the router let through can still fail to convert to. The
    /// registry holds the registration to one of the named constraints, and
    /// <see cref="RequiredTemplate"/> is the same fact written as the template the document is
    /// then written from.
    /// </para>
    /// <para>
    /// A type no constraint guarantees gets no alternatives and is not held to anything. Its
    /// operation keeps the 400 its converter can still answer.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BoundTokens(
        IReadOnlyList<RequestParameterInformation> parameters
    ) =>
        PathParameters(parameters)
            .Select(parameter =>
            {
                var names = RouteConstraintFacts.SatisfyingNames(parameter.ParameterType.Name);

                return names.Count == 0
                    ? parameter.Name
                    : parameter.Name + ":" + string.Join("|", names);
            })
            .ToList();

    /// <summary>
    /// The route template this handler has to be registered under, as the tokens it reads and the
    /// constraint each of them needs.
    /// </summary>
    /// <remarks>
    /// Stands in for the declared path a controller's handler has, which is what the document
    /// writer reads to decide whether a token can refuse a value. A lambda had a placeholder
    /// there, so every registered operation published the 400 the converter would answer and not
    /// the 404 the constraint produces instead - the reverse of what the same handler publishes
    /// when it is declared with an attribute. The registry refuses a registration that does not
    /// carry these constraints, so what is written here is true of every path this is served at.
    /// </remarks>
    private static string RequiredTemplate(IReadOnlyList<RequestParameterInformation> parameters) =>
        string.Concat(
            PathParameters(parameters)
                .Select(parameter =>
                {
                    var names = RouteConstraintFacts.SatisfyingNames(parameter.ParameterType.Name);

                    return names.Count == 0
                        ? "/{" + parameter.Name + "}"
                        : "/{" + parameter.Name + ":" + names[0] + "}";
                })
        );

    private static IEnumerable<RequestParameterInformation> PathParameters(
        IReadOnlyList<RequestParameterInformation> parameters
    ) => parameters.Where(parameter => parameter.BindingType == ParameterBindType.Path);

    /// <summary>Where the handler class is emitted, beside the type that registered it.</summary>
    private static string HandlerNamespace(GeneratorSyntaxContext context) =>
        context.SemanticModel.Compilation.AssemblyName is { Length: > 0 } assembly
            ? assembly + ".Generated"
            : "Generated";

    /// <summary>
    /// A name unique to this call site and stable across builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From the enclosing member and a hash of the location rather than from a counter: a counter
    /// renumbers every handler below an inserted registration, which dirties the incremental cache
    /// for all of them and rewrites every emitted file.
    /// </para>
    /// <para>
    /// <b>The hash is written out rather than taken from <c>string.GetHashCode</c>.</b> That one is
    /// randomised per process, so the same source produced a differently named class on every
    /// build - which is not a reproducible build, and which the golden fixtures caught by
    /// disagreeing with themselves between two runs.
    /// </para>
    /// <para>
    /// <b>And it is seeded with the file name rather than the path</b>, because a deterministic
    /// build rewrites source paths and the name would otherwise differ between a developer's
    /// machine and CI. Two files of the same name in different folders is the case this cannot
    /// separate, and the enclosing member and the offset separate it instead - a collision would be
    /// a duplicate class rather than anything silent.
    /// </para>
    /// </remarks>
    private static string HandlerName(InvocationExpressionSyntax invocation, string verb)
    {
        var enclosing = invocation
            .Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .Select(Named)
            .FirstOrDefault(name => name != null);

        // The file name rather than the path. A deterministic build rewrites source paths to /_/…,
        // so hashing the path gave one name on a developer's machine and another in CI - which the
        // golden fixtures caught by disagreeing across environments rather than across runs.
        var hash = Fnv(
            System.IO.Path.GetFileName(invocation.SyntaxTree.FilePath) ?? "",
            invocation.GetLocation().SourceSpan.Start
        );

        return (enclosing ?? "Registered") + "_" + verb + "_" + hash.ToString("x8");
    }

    /// <summary>FNV-1a, because it is a few lines and does not move.</summary>
    private static uint Fnv(string text, int start)
    {
        unchecked
        {
            var hash = 2166136261;

            foreach (var character in text)
            {
                hash = (hash ^ character) * 16777619;
            }

            hash = (hash ^ (uint)start) * 16777619;

            return hash;
        }
    }

    private static string? Named(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            TypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => null,
        };
}
