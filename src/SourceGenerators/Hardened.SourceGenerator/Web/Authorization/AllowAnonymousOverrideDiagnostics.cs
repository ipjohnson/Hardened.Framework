using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Authorization;

/// <summary>
/// A requirement written on a handler's method that <c>[AllowAnonymous]</c> cancels.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> wins over every requirement, so the handler is public and the attribute
/// on its method does nothing. Nothing at run time says so. A stray <c>[AllowAnonymous]</c>, left on
/// after debugging or sitting on the class, disables a grant somebody wrote on purpose.
/// </para>
/// <para>
/// Reported only when the cancelled requirement is on the method. <c>[AllowAnonymous]</c> on a
/// method under a class's requirement is how one route in a guarded class is made public, and is
/// not reported.
/// </para>
/// <para>
/// A warning, with the same levers as <see cref="RequireAuthorizationDiagnostics"/>:
/// <c>&lt;NoWarn&gt;</c> works, and <c>#pragma</c> does not.
/// </para>
/// </remarks>
public static class AllowAnonymousOverrideDiagnostics
{
    public const string DiagnosticId = "HAUTH002";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <see cref="RequireAuthorizationDiagnostics"/> gives.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "[AllowAnonymous] cancels a requirement on the same handler",
            messageFormat: "'{0}' carries an authorization attribute on its method and [AllowAnonymous] on the method "
                + "or its class. [AllowAnonymous] wins, so the handler is public and the attribute has no effect. "
                + "Remove the one that is not meant.",
            category: "Hardened.Authorization",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    /// <summary>Reports one handler, if it needs reporting.</summary>
    public static void Report(SourceProductionContext context, HandlerAuthorizationModel handler)
    {
        if (!handler.AnonymousOverridesItsMethod)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptor(),
                handler.DeclaredAt?.ToLocation() ?? Location.None,
                handler.Handler
            )
        );
    }
}
