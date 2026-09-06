using System.Collections.Generic;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// The three ways a described operation ends up doing less than its implementation says, with a
/// clean build.
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

    /// <summary>A declaration the described path does not read, written on a handler method.</summary>
    public const string InertDeclarationId = "HOAG032";

    /// <summary>
    /// The declarations that are code-first syntax only, by attribute name.
    /// </summary>
    /// <remarks>
    /// <c>[RawResponse]</c> commits a response to a content type, and the generator reads it off
    /// the handler's own syntax. A described operation's signature is generated, so there is no
    /// syntax to read it from: the attribute compiles on the implementation, reads as a commitment
    /// in review, and changes nothing. A contract says the same thing with the response's media
    /// type.
    /// </remarks>
    private static readonly Dictionary<string, string> InertDeclarations = new(StringComparer.Ordinal) {
        ["RawResponseAttribute"] =
            "the content type a described response commits to comes from the contract's media type"
    };

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

    private static DiagnosticDescriptor InertDeclarationDescriptor() => new(
        id: InertDeclarationId,
        title: "Declaration is not read on a described handler",
        messageFormat:
        "'{0}.{1}' carries [{2}], which is read from a handler's own syntax and a described " +
        "operation's signature is generated - so it compiles, reads as a commitment, and changes " +
        "nothing. Remove it: {3}.",
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

            ReportInertDeclarations(handler, diagnostics);

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

    /// <summary>
    /// Every declaration on this handler's methods that the described path does not read.
    /// </summary>
    /// <remarks>
    /// Read off the filters <c>HandlerSelector</c> already collected rather than from a walk of
    /// its own, so an attribute added to <see cref="InertDeclarations"/> is one line. The location
    /// is the class's, which is what <c>HandlerInfo</c> carries; the message names the method.
    /// </remarks>
    private static void ReportInertDeclarations(HandlerInfo handler, List<Diagnostic> diagnostics) {
        foreach (var method in handler.MethodFilters) {
            foreach (var filter in method.Filters) {
                if (!InertDeclarations.TryGetValue(filter.TypeDefinition.Name, out var instead)) {
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    InertDeclarationDescriptor(),
                    handler.Location ?? Location.None,
                    handler.ImplementationType.Name,
                    method.MethodName,
                    filter.TypeDefinition.Name.Replace("Attribute", ""),
                    instead));
            }
        }
    }
}
