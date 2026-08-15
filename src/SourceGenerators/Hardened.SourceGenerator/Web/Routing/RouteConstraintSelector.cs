using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// One <c>[RouteConstraint]</c> method the application declared.
/// </summary>
/// <remarks>
/// Compared by value, so the model still keys the incremental cache: adding a constraint rebuilds
/// the tables, and editing an unrelated method does not.
/// </remarks>
public class RouteConstraintModel : IEquatable<RouteConstraintModel> {
    public RouteConstraintModel(string name, string call, bool signatureIsValid, string declaredBy) {
        Name = name;
        Call = call;
        SignatureIsValid = signatureIsValid;
        DeclaredBy = declaredBy;
    }

    /// <summary>The name a route template uses after the colon, lower-cased.</summary>
    public string Name { get; }

    /// <summary>The fully qualified method group the generated table calls.</summary>
    public string Call { get; }

    /// <summary>
    /// Whether it is a <c>static bool(ReadOnlySpan&lt;char&gt;)</c>. False is reported rather than
    /// emitted: a call to the wrong signature is a CS error in generated code, which reads as a
    /// generator defect rather than as the mistake it is.
    /// </summary>
    public bool SignatureIsValid { get; }

    /// <summary>Where it was declared, for the diagnostic.</summary>
    public string DeclaredBy { get; }

    public bool Equals(RouteConstraintModel? other) =>
        other != null &&
        Name == other.Name &&
        Call == other.Call &&
        SignatureIsValid == other.SignatureIsValid &&
        DeclaredBy == other.DeclaredBy;

    public override bool Equals(object obj) => Equals(obj as RouteConstraintModel);

    public override int GetHashCode() {
        unchecked {
            var hash = Name.GetHashCode();

            hash = (hash * 397) ^ Call.GetHashCode();
            hash = (hash * 397) ^ SignatureIsValid.GetHashCode();

            return hash;
        }
    }
}

/// <summary>
/// Finds the <c>[RouteConstraint]</c> methods in a compilation.
/// </summary>
public static class RouteConstraintSelector {
    private const string AttributeName = "RouteConstraint";

    /// <summary>
    /// <c>HRDR003</c> - a constraint declared on something the table cannot call.
    /// </summary>
    public const string SignatureDiagnosticId = "HRDR003";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>UnresolvedHandler.Descriptor</c> is: RS2008 looks for the field, and these projects set
    /// <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor SignatureDescriptor() => new(
        id: SignatureDiagnosticId,
        title: "Route constraint has the wrong signature",
        messageFormat:
        "'{0}' declares the route constraint '{1}' but is not a static bool(ReadOnlySpan<char>). " +
        "The span is the rule rather than a preference: a constraint runs on every request that " +
        "reaches the position it guards, including the ones it rejects, so a string parameter " +
        "would allocate to decide that a request does not match.",
        category: "Hardened.Routing",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports every declaration the table cannot call.
    /// </summary>
    /// <remarks>
    /// Off the collected constraints rather than per handler or per entry point, so a wrong
    /// signature is reported once however many routes or modules an assembly has.
    /// </remarks>
    public static void ReportInvalidSignatures(
        SourceProductionContext context, IReadOnlyList<RouteConstraintModel> constraints) {
        foreach (var constraint in constraints) {
            if (constraint.SignatureIsValid) {
                continue;
            }

            // Location.None, on the same terms as every other diagnostic reported off a model: a
            // syntax location would travel through the incremental caches, which compare models
            // for equality to decide whether to rerun.
            context.ReportDiagnostic(Diagnostic.Create(
                SignatureDescriptor(), Location.None, constraint.DeclaredBy, constraint.Name));
        }
    }

    public static bool Predicate(SyntaxNode node, CancellationToken cancellationToken) =>
        node is MethodDeclarationSyntax method && method.AttributeLists.Count > 0;

    public static IReadOnlyList<RouteConstraintModel> Transform(
        GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var method = (MethodDeclarationSyntax)context.Node;

        List<RouteConstraintModel>? constraints = null;

        foreach (var attributeList in method.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                cancellationToken.ThrowIfCancellationRequested();

                var name = attribute.Name.ToString();

                if (name != AttributeName && name != AttributeName + "Attribute") {
                    continue;
                }

                var declared = attribute.GetFirstStringArgumentValue(context);

                if (string.IsNullOrEmpty(declared)) {
                    continue;
                }

                constraints ??= new List<RouteConstraintModel>();

                constraints.Add(new RouteConstraintModel(
                    declared.ToLowerInvariant(),
                    Call(method),
                    IsValidSignature(context, method),
                    Owner(method) + "." + method.Identifier.Text));
            }
        }

        return (IReadOnlyList<RouteConstraintModel>?)constraints ?? Array.Empty<RouteConstraintModel>();
    }

    /// <summary>
    /// <c>static bool(ReadOnlySpan&lt;char&gt;)</c>, checked syntactically.
    /// </summary>
    /// <remarks>
    /// The span is the rule rather than a preference: a constraint runs on every request that
    /// reaches the position it guards, including the ones it rejects, so a <c>string</c> parameter
    /// would allocate to decide that a request does not match.
    /// </remarks>
    private static bool IsValidSignature(GeneratorSyntaxContext context, MethodDeclarationSyntax method) {
        if (!method.Modifiers.Any(modifier => modifier.Text == "static")) {
            return false;
        }

        if (method.ReturnType.ToString() != "bool" && method.ReturnType.ToString() != "Boolean") {
            return false;
        }

        if (method.ParameterList.Parameters.Count != 1) {
            return false;
        }

        var parameterType = method.ParameterList.Parameters[0].Type?.ToString() ?? "";

        return parameterType.Replace(" ", "").EndsWith("ReadOnlySpan<char>", StringComparison.Ordinal);
    }

    /// <summary>The fully qualified method group, for generated code that carries no usings.</summary>
    private static string Call(MethodDeclarationSyntax method) =>
        "global::" + Owner(method) + "." + method.Identifier.Text;

    private static string Owner(MethodDeclarationSyntax method) {
        var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var containing = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        var name = type?.Identifier.Text ?? "";
        var ns = containing?.Name.ToFullString().TrimEnd() ?? "";

        return ns.Length == 0 ? name : ns + "." + name;
    }
}
