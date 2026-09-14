using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
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

        var handler = new RequestHandlerModel(
            // The path the handler reports before it is constructed, which nothing reads: the
            // registry always builds it with the template it registered, and WithPath replaces
            // this.
            new RequestHandlerNameModel("(registered)", verb),
            delegateType,
            "Invoke",
            TypeDefinition.Get(HandlerNamespace(context), HandlerName(invocation, verb)),
            parameters,
            Response(lambda),
            Filters(context, lambdaSyntax, cancellationToken)
        )
        {
            IsDelegateHandler = true,
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
        var arguments = lambda
            .Parameters.Select(parameter =>
                TypeSyntaxExtensions.GetTypeDefinitionFromType(parameter.Type)
            )
            .ToList();

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

        arguments.Add(TypeSyntaxExtensions.GetTypeDefinitionFromType(lambda.ReturnType));

        return new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition,
            "System",
            "Func",
            arguments
        );
    }

    private static ResponseInformationModel Response(IMethodSymbol lambda)
    {
        if (lambda.ReturnsVoid)
        {
            return new ResponseInformationModel { ReturnType = TypeDefinition.Get(typeof(void)) };
        }

        var returnType = TypeSyntaxExtensions.GetTypeDefinitionFromType(lambda.ReturnType);

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

    private static IReadOnlyList<string> BoundTokens(
        IReadOnlyList<RequestParameterInformation> parameters
    ) =>
        parameters
            .Where(parameter => parameter.BindingType == ParameterBindType.Path)
            .Select(parameter => parameter.Name)
            .ToList();

    /// <summary>Where the handler class is emitted, beside the type that registered it.</summary>
    private static string HandlerNamespace(GeneratorSyntaxContext context) =>
        context.SemanticModel.Compilation.AssemblyName is { Length: > 0 } assembly
            ? assembly + ".Generated"
            : "Generated";

    /// <summary>
    /// A name unique to this call site and stable across builds.
    /// </summary>
    /// <remarks>
    /// From the enclosing member and a hash of the location rather than from a counter: a counter
    /// renumbers every handler below an inserted registration, which dirties the incremental cache
    /// for all of them and rewrites every emitted file.
    /// </remarks>
    private static string HandlerName(InvocationExpressionSyntax invocation, string verb)
    {
        var enclosing = invocation
            .Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .Select(Named)
            .FirstOrDefault(name => name != null);

        var span = invocation.GetLocation().SourceSpan;
        var file = invocation.SyntaxTree.FilePath;
        unchecked
        {
            var hash = (uint)((file.GetHashCode() * 397) ^ span.Start);

            return (enclosing ?? "Registered") + "_" + verb + "_" + hash.ToString("x8");
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
