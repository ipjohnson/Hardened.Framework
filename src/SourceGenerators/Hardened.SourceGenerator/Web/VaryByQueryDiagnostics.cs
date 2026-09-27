using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// A <c>VaryByQuery</c> key that names no query key the operation binds.
/// </summary>
/// <remarks>
/// <para>
/// The strategy reads only the keys it names. A misspelt key reads an empty value on every
/// request, and the key the author meant is left out of the entry. In the 0.41 trial a catalogue
/// named <c>cursr</c> for <c>cursor</c>, built clean, and served the first page for the second
/// until the entry expired. The operation's parameters say which query keys it binds, whether
/// they were written in C# or read from a contract, so the build can check.
/// </para>
/// <para>
/// <b>A warning, not an error.</b> A handler can read a query value it does not bind through the
/// request itself, and key on it on purpose. A key is checked when it is a constant string, such as
/// a literal or a <c>const</c>. Any other argument is left alone.
/// </para>
/// </remarks>
public static class VaryByQueryDiagnostics
{
    public const string DiagnosticId = "HRDW009";

    private const string AttributeNamespace = "Hardened.Requests.Runtime.Caching";

    private const string AttributeName = "CacheResponseAttribute";

    private const string StrategyNamespace = "Hardened.Web.Runtime.Caching";

    private const string StrategyName = "VaryByQuery";

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() =>
        new(
            id: DiagnosticId,
            title: "A VaryByQuery key names no query key the operation binds",
            messageFormat: "'{0}' keys its cached response on the query key '{1}', which none of its "
                + "parameters binds, so requests that differ in a key it does bind can get the same "
                + "entry. The query keys it binds are {2}. [CacheResponse<VaryByQuery>] with no keys "
                + "varies on all of them.",
            category: "Hardened.Web",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

    /// <summary>
    /// The keys this handler's <c>VaryByQuery</c> declarations name that it binds no query key
    /// for, each once, in the order they were written.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Report"/> because a <c>SourceProductionContext</c> only exists
    /// inside a running generator, and the keys are worth testing on their own.
    /// </remarks>
    public static IReadOnlyList<string> UnboundKeys(RequestHandlerModel model)
    {
        List<string>? bound = null;
        List<string>? unbound = null;

        foreach (var filter in model.Filters)
        {
            if (!IsVaryByQuery(filter.TypeDefinition) || filter.ConstantStrings.Length == 0)
            {
                continue;
            }

            bound ??= HandlerInfoCodeGenerator.QueryParameters(model);

            foreach (var key in filter.ConstantStrings.Split('\u001f'))
            {
                if (
                    !bound.Contains(key, StringComparer.Ordinal)
                    && !(unbound ??= new List<string>()).Contains(key, StringComparer.Ordinal)
                )
                {
                    unbound.Add(key);
                }
            }
        }

        return (IReadOnlyList<string>?)unbound ?? Array.Empty<string>();
    }

    public static void Report(SourceProductionContext context, RequestHandlerModel model)
    {
        var unbound = UnboundKeys(model);

        if (unbound.Count == 0)
        {
            return;
        }

        var bound = HandlerInfoCodeGenerator.QueryParameters(model);
        var listed =
            bound.Count == 0 ? "none" : string.Join(", ", bound.Select(key => "'" + key + "'"));

        foreach (var key in unbound)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    Descriptor(),
                    Location.None,
                    model.ControllerType.Name + "." + model.HandlerMethod,
                    key,
                    listed
                )
            );
        }
    }

    /// <summary>
    /// <c>[CacheResponse&lt;VaryByQuery&gt;]</c>, and no other strategy.
    /// </summary>
    private static bool IsVaryByQuery(ITypeDefinition type) =>
        type is GenericTypeDefinition { TypeArguments.Count: 1 } generic
        && type.Name == AttributeName
        && type.Namespace == AttributeNamespace
        && generic.TypeArguments[0].Name == StrategyName
        && generic.TypeArguments[0].Namespace == StrategyNamespace;
}
