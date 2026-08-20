using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// The two ways a described operation ends up with no code behind it and a clean build.
/// </summary>
/// <remarks>
/// <para>
/// Both are warnings rather than errors, and deliberately. Generating the interfaces without
/// implementing them is a supported thing to do - a package that carries a contract for a client to
/// consume, or one project describing a service another implements - so an error would make a
/// legitimate target impossible to build.
/// </para>
/// <para>
/// <c>TreatWarningsAsErrors</c> is set for continuous-integration builds, so a project that means to
/// ship interfaces alone silences these in its own csproj:
/// <c>&lt;NoWarn&gt;$(NoWarn);HOAG030;HOAG031&lt;/NoWarn&gt;</c>. That is the escape hatch, and it
/// is deliberate that it has to be written down rather than inferred.
/// </para>
/// </remarks>
internal static class HandlerBindingDiagnostics {

    /// <summary>A described service that nothing implements.</summary>
    public const string NoHandlerId = "HOAG030";

    /// <summary>A handler whose base list names no described service.</summary>
    public const string NoServiceInterfaceId = "HOAG031";

    /// <summary>
    /// Built per call rather than held in a static field, for the RS2008 reason the other
    /// descriptors in this repository are - see <c>UnresolvedHandler</c>.
    /// </summary>
    private static DiagnosticDescriptor NoHandlerDescriptor() => new(
        id: NoHandlerId,
        title: "Described service has no handler",
        messageFormat:
        "'{0}' is declared by the description but no class carrying [Handler] implements it, so " +
        "its {1} route(s) exist and fail at request time. Implement it, or set " +
        "<NoWarn>$(NoWarn);" + NoHandlerId + "</NoWarn> if this project ships the interface alone.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static DiagnosticDescriptor NoServiceInterfaceDescriptor() => new(
        id: NoServiceInterfaceId,
        title: "Handler implements no described service",
        messageFormat:
        "'{0}' carries [Handler] but its base list names no service the description declares - " +
        "it lists {1}. It is registered against '{2}', which routes nothing. A base class has to " +
        "be first in C#, so the service interface is found by name rather than by position.",
        category: "Hardened.Generation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports both directions of the match onto <paramref name="context"/>.
    /// </summary>
    public static void Report(
        SourceProductionContext context,
        IReadOnlyList<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlers) {
        foreach (var diagnostic in Collect(models, handlers, context.CancellationToken)) {
            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Both directions of the match, given everything the description declared and every
    /// <c>[Handler]</c> in the compilation.
    /// </summary>
    /// <remarks>
    /// Returned rather than reported, so the decision is testable. <c>SourceProductionContext</c> is
    /// a struct Roslyn alone can construct, and a rule that can only be exercised by running a whole
    /// generator tends not to be exercised at all.
    /// </remarks>
    public static IReadOnlyList<Diagnostic> Collect(
        IReadOnlyList<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlers,
        CancellationToken cancellationToken = default) {
        var diagnostics = new List<Diagnostic>();

        if (models.Count == 0) {
            // No description in this project at all, so neither direction means anything. A
            // hand-written [Handler] belongs to the other generator.
            return diagnostics;
        }

        var declaredNames = new HashSet<string>(models.Select(model => model.ControllerType.Name));
        var implementedNames = new HashSet<string>();

        foreach (var handler in handlers) {
            cancellationToken.ThrowIfCancellationRequested();

            var service = handler.ServiceInterface(declaredNames);

            if (service != null) {
                implementedNames.Add(service.Name);
                continue;
            }

            diagnostics.Add(Diagnostic.Create(
                NoServiceInterfaceDescriptor(),
                handler.Location ?? Location.None,
                handler.ImplementationType.Name,
                string.Join(", ", handler.InterfaceCandidates.Select(candidate => candidate.Name)),
                handler.InterfaceType.Name));
        }

        // Ordered, so a project with two missing handlers reports them the same way every build.
        foreach (var name in declaredNames.OrderBy(name => name, StringComparer.Ordinal)) {
            if (implementedNames.Contains(name)) {
                continue;
            }

            var routeCount = models.Count(model => model.ControllerType.Name == name);

            diagnostics.Add(Diagnostic.Create(
                NoHandlerDescriptor(), Location.None, name, routeCount));
        }

        return diagnostics;
    }
}
