using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// A handler that declares <c>[Compress]</c> more than once.
/// </summary>
/// <remarks>
/// <para>
/// The attribute does not allow multiples on one element, so the compiler already refuses two of
/// the same form there. What it cannot see is a declaration on the class and another on one of
/// its methods, or <c>[Compress]</c> beside <c>[Compress&lt;T&gt;]</c>, which are different
/// attribute types. Both reach the handler's metadata, and at run time the method's filter wraps
/// first and the class's finds the body already wrapped and stands down - so the method wins,
/// silently, which is behaviour nobody reading the class can see.
/// </para>
/// <para>
/// An error rather than an ordering rule, like <see cref="Requests.FormAndBodyDiagnostics"/>: the
/// declaration says what the author wanted, and two of them say two things.
/// </para>
/// </remarks>
public static class CompressDiagnostics {
    public const string DiagnosticId = "HRDW003";

    private const string AttributeNamespace = "Hardened.Web.Runtime.Compression";

    private const string AttributeName = "CompressAttribute";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Handler declares [Compress] more than once",
        messageFormat:
        "'{0}' carries {1} [Compress] declarations - on the method and on its class, or both " +
        "[Compress] and [Compress<T>]. One declaration decides how an operation is compressed, " +
        "so remove the others.",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// How many compress declarations reach this handler, from its method and its class together.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> because a <c>SourceProductionContext</c> only exists
    /// inside a running generator, and the count is worth testing on its own.
    /// </remarks>
    public static int Declarations(RequestHandlerModel model) {
        var count = 0;

        foreach (var filter in model.Filters) {
            if (IsCompress(filter.TypeDefinition)) {
                count++;
            }
        }

        return count;
    }

    public static void Report(SourceProductionContext context, RequestHandlerModel model) {
        var declarations = Declarations(model);

        if (declarations < 2) {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                Descriptor(),
                Location.None,
                model.ControllerType.Name + "." + model.HandlerMethod,
                declarations));
    }

    /// <summary>
    /// Either form. The generic one is modelled as a <c>GenericTypeDefinition</c> with the same
    /// namespace and name, so one comparison covers both.
    /// </summary>
    private static bool IsCompress(ITypeDefinition type) =>
        type.Name == AttributeName && type.Namespace == AttributeNamespace;
}
