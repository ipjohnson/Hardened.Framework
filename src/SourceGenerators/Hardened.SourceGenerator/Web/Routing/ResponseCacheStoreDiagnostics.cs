using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Routing;

/// <summary>
/// A handler declaring <c>[CacheResponse]</c> in an application that registers no store.
/// </summary>
/// <remarks>
/// <para>
/// The attribute alone does nothing. The store is a separate package, applied as a module attribute
/// on the entry point, and an application that declares the one without the other builds clean and
/// answers every cached route with an error at run time. Every arm of the 0.19 trial made this
/// mistake, and each found out from a request rather than from a build.
/// </para>
/// <para>
/// <b>A warning, not an error.</b> The check reads the entry point's module attributes, which is
/// where <c>[HardenedMemoryResponseCache]</c> goes and where <c>[RequireAuthorization]</c> is
/// already read from. A store registered by hand inside <c>ConfigureServices</c> is invisible to
/// it, and that is a legitimate arrangement - so this says "nothing here registers one" and lets
/// an author who knows better carry on.
/// </para>
/// </remarks>
public static class ResponseCacheStoreDiagnostics {
    public const string DiagnosticId = "HRDW005";

    private const string AttributeNamespace = "Hardened.Requests.Runtime.Caching";

    private const string AttributeName = "CacheResponseAttribute";

    /// <summary>
    /// The module attributes that bring a store with them. Named rather than discovered: probing
    /// the compilation for an <c>IResponseCacheStore</c> implementation would answer for a type a
    /// reference merely contains, which is not the same as one this application registered.
    /// </summary>
    private static readonly string[] StoreModuleAttributes = {
        "HardenedMemoryResponseCacheAttribute"
    };

    /// <summary>
    /// Built per call rather than held in a static field, for the reason
    /// <c>AmbiguousRouteDiagnostics.Descriptor</c> is: RS2008 looks for the field, and these
    /// projects set <c>EnforceExtendedAnalyzerRules</c>.
    /// </summary>
    private static DiagnosticDescriptor Descriptor() => new(
        id: DiagnosticId,
        title: "Response caching is declared and no store is registered",
        messageFormat:
        "'{0}' declares [CacheResponse] and this application registers no response cache store, " +
        "so every request to it answers an error. Add the Hardened.Requests.Caching.Memory " +
        "package and [HardenedMemoryResponseCache] to the module, or register an " +
        "IResponseCacheStore yourself and suppress " + DiagnosticId + ".",
        category: "Hardened.Web",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>Whether a handler asks for its response to be cached.</summary>
    /// <remarks>
    /// The generic form is a <c>GenericTypeDefinition</c> with the same namespace and name, so one
    /// comparison covers <c>[CacheResponse]</c> and <c>[CacheResponse&lt;T&gt;]</c> alike.
    /// </remarks>
    public static bool DeclaresCaching(RequestHandlerModel model) =>
        model.Filters.Any(filter => IsCacheResponse(filter.TypeDefinition));

    /// <summary>
    /// Whether the entry point applies a module that registers a store.
    /// </summary>
    /// <remarks>
    /// Off the entry point's attributes, which carry the class's and the assembly's both - the same
    /// place <c>[BasePath]</c> and <c>[RequireAuthorization]</c> are read from.
    /// </remarks>
    public static bool RegistersAStore(EntryPointSelector.Model applicationModel) =>
        applicationModel.AttributeModels?.Any(
            attribute => StoreModuleAttributes.Contains(attribute.TypeDefinition.Name)) ?? false;

    /// <summary>
    /// One report per assembly, naming every handler that would fail.
    /// </summary>
    /// <remarks>
    /// The missing store is one mistake however many handlers depend on it, and a report per
    /// handler would bury that under a list saying the same thing. The names are in the message so
    /// the author still knows what stops working.
    /// </remarks>
    public static void Report(
        SourceProductionContext context,
        EntryPointSelector.Model applicationModel,
        IReadOnlyList<RequestHandlerModel> handlers) {
        if (RegistersAStore(applicationModel)) {
            return;
        }

        var declaring = handlers
            .Where(DeclaresCaching)
            .Select(handler => handler.ControllerType.Name + "." + handler.HandlerMethod)
            .ToList();

        if (declaring.Count == 0) {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Descriptor(), Location.None, string.Join(", ", declaring)));
    }

    private static bool IsCacheResponse(ITypeDefinition type) =>
        type.Name == AttributeName && type.Namespace == AttributeNamespace;
}
