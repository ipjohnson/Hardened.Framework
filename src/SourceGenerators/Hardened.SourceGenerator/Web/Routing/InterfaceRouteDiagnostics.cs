using System.Linq;
using System.Threading;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A verb attribute on an interface member, which declares a route and compiles to nothing.
/// </summary>
/// <remarks>
/// <para>
/// The generator routes a method it can call, and an interface member has no implementation to
/// call. Until the selector was taught that, such a declaration threw out of the syntax transform
/// and cost the assembly every route it had. Skipping it silently would trade that for the other
/// failure the routing diagnostics exist to prevent: a declaration that reads in review as a route
/// the application serves, and answers 404.
/// </para>
/// <para>
/// A warning rather than an error, like the rest of the HRDR warnings, and
/// <c>TreatWarningsAsErrors</c> is on for continuous-integration builds - so it cannot merge while
/// still not blocking a refactor in progress.
/// </para>
/// </remarks>
public static class InterfaceRouteDiagnostics {
    public const string DiagnosticId = "HRDR013";

    /// <summary>
    /// The namespace Hardened's own verb attributes are declared in.
    /// </summary>
    /// <remarks>
    /// Checked, because the names are not Hardened's alone. Refit declares <c>[Get]</c>,
    /// <c>[Post]</c> and the rest on exactly the shape this looks at - an interface method - and a
    /// Refit client interface sharing a project with a Hardened test host is correct code that
    /// must not warn. The selector matches the bare name and cannot tell them apart; this resolves
    /// the attribute and can.
    /// </remarks>
    private const string VerbNamespace = "Hardened.Web.Runtime.Attributes";

    private static readonly string[] VerbAttributeNames = {
        "GetAttribute", "PostAttribute", "PutAttribute", "PatchAttribute", "DeleteAttribute"
    };

    /// <summary>
    /// Built per call rather than held in a static field, for the RS2008 reason every other
    /// descriptor in this assembly is - see <c>UnresolvedHandler</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Route declared on an interface",
        messageFormat:
        "'{0}' carries [{1}] on an interface member, which has no implementation to call, so no " +
        "route is compiled for it. Move the attribute to the class that implements it.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Syntax only, and deliberately wider than what is reported: every interface method carrying
    /// an attribute whose bare name is a verb. <see cref="Transform"/> resolves it and drops the
    /// ones that belong to another library.
    /// </summary>
    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) {
        if (node is not MethodDeclarationSyntax method ||
            method.Parent is not InterfaceDeclarationSyntax) {
            return false;
        }

        foreach (var list in method.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var name = attribute.Name.ToString();
                var simple = name.Substring(name.LastIndexOf('.') + 1);

                if (!simple.EndsWith("Attribute")) {
                    simple += "Attribute";
                }

                if (Array.IndexOf(VerbAttributeNames, simple) >= 0) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The declaration, or null where the attribute turned out to be another library's.
    /// </summary>
    public static InterfaceRouteModel? Transform(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var method = (MethodDeclarationSyntax)context.Node;
        var declaringInterface = (InterfaceDeclarationSyntax)method.Parent!;

        foreach (var list in method.AttributeLists) {
            foreach (var attribute in list.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var type = context.SemanticModel.GetTypeInfo(attribute, cancellationToken).Type;

                if (type == null ||
                    Array.IndexOf(VerbAttributeNames, type.Name) < 0 ||
                    type.ContainingNamespace?.ToDisplayString() != VerbNamespace) {
                    continue;
                }

                return new InterfaceRouteModel(
                    declaringInterface.Identifier.Text,
                    method.Identifier.Text,
                    type.Name,
                    // The identifier rather than the whole declaration, so the squiggle lands on
                    // the method name instead of underlining the signature.
                    LocationInfo.From(method.Identifier));
            }
        }

        return null;
    }

    public static void Report(SourceProductionContext context, InterfaceRouteModel? model) {
        if (model == null) {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptor(),
            model.DeclaredAt?.ToLocation() ?? Location.None,
            model.Handler,
            model.AttributeName));
    }
}

/// <summary>
/// What the diagnostic needs and nothing else, carrying a location because nothing downstream of
/// it emits source - the trade <see cref="LocationInfo"/> describes.
/// </summary>
public record InterfaceRouteModel(
    string InterfaceName,
    string MethodName,
    string AttributeName,
    LocationInfo? DeclaredAt) {

    public string Handler => InterfaceName + "." + MethodName;
}
