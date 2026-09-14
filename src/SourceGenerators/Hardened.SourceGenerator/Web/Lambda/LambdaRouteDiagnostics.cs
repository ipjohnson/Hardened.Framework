using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Lambda;

/// <summary>One registration the build cannot read, and why.</summary>
public class UnreadableRegistrationModel : IEquatable<UnreadableRegistrationModel>
{
    public UnreadableRegistrationModel(Location location, string reason)
    {
        Location = location;
        Reason = reason;
    }

    public Location Location { get; }

    public string Reason { get; }

    public bool Equals(UnreadableRegistrationModel? other) =>
        other != null && Location.Equals(other.Location) && Reason == other.Reason;

    public override bool Equals(object obj) => Equals(obj as UnreadableRegistrationModel);

    public override int GetHashCode() =>
        unchecked((Location.GetHashCode() * 397) ^ Reason.GetHashCode());
}

/// <summary>
/// Reports a lambda registration the generator cannot emit a handler for.
/// </summary>
/// <remarks>
/// <para>
/// The declared methods throw, so none of these ships a route that silently binds nothing. What
/// they do without this is fail at startup, in an application that built clean - and the thing to
/// change is at the call site, which the build can point at and the exception cannot.
/// </para>
/// <para>
/// Its own syntax provider, carrying a location, feeding nothing that emits source. That is the
/// arrangement <c>RequireAuthorizationDiagnostics</c> uses and the reason it gives: a span is an
/// offset, every offset below an edit shifts, and a model that carries one would invalidate every
/// handler in the file on a comment.
/// </para>
/// </remarks>
public static class LambdaRouteDiagnostics
{
    public const string DiagnosticId = "HRDR014";

    private const string RegistryName = "IRouteRegistry";

    private const string RegistryNamespace = "Hardened.Web.Runtime.Routing";

    /// <summary>
    /// Built per call rather than held in a static field: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "Route registration the build cannot read",
            messageFormat: "This route cannot be registered: {0}",
            category: "Hardened.Routing",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    /// <remarks>
    /// Every invocation whose name could be a registration. The transform binds and decides, and
    /// answers null for the overwhelming majority.
    /// </remarks>
    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) =>
        node
            is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax member,
                ArgumentList.Arguments: var arguments,
            }
        && LambdaRouteSelector.Named(member, arguments.Count);

    public static UnreadableRegistrationModel? Transform(
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

        // The controller overloads take a Type and a method name and are read by nothing here.
        if (
            registration.Parameters.LastOrDefault()?.Type.SpecialType != SpecialType.System_Delegate
        )
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var handler = arguments[arguments.Count - 1].Expression;

        if (handler is not AnonymousFunctionExpressionSyntax)
        {
            return new UnreadableRegistrationModel(
                handler.GetLocation(),
                "the handler has to be a lambda written here. The build reads the lambda to emit a "
                    + "handler for it, and a delegate held in a variable or returned from a method "
                    + "is not something it can read"
            );
        }

        if (LambdaRouteSelector.Verb(context, member, arguments, cancellationToken) == null)
        {
            return new UnreadableRegistrationModel(
                arguments[0].Expression.GetLocation(),
                "the verb has to be a constant. It is written into the handler's own information "
                    + "and into the table the route joins, so it cannot come from a variable"
            );
        }

        return null;
    }

    public static void Report(SourceProductionContext context, UnreadableRegistrationModel? finding)
    {
        if (finding == null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor(), finding.Location, finding.Reason));
    }
}
