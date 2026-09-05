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
/// <para>
/// <b>Asked of the application, not of a library.</b> The template splits an application into a
/// library that declares the handlers and a host that applies the runtime, and the store goes
/// beside the runtime. Asked of the library, this warned whatever the host had registered - the
/// 0.20 trial's first finding against the template's own layout. So a compilation whose entry
/// point applies no web runtime says nothing, and the compilation that does is handed the cached
/// handlers of every module it imports, read from their metadata, so the question is still
/// answered for the layout that split it.
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
    /// The module attributes that make an entry point an application: the web runtimes this
    /// repository ships, and the Lambda web runtime Hardened.Amz ships. Named for the reason the
    /// store attributes are.
    /// </summary>
    private static readonly string[] WebRuntimeModuleAttributes = {
        "KestrelRuntimeAttribute",
        "AspNetCoreRuntimeAttribute",
        "LambdaWebModuleAttribute"
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
    /// Whether the entry point applies a module that registers a store, itself or through a
    /// module it imports.
    /// </summary>
    /// <remarks>
    /// Off the entry point's attributes, which carry the class's and the assembly's both - the same
    /// place <c>[BasePath]</c> and <c>[RequireAuthorization]</c> are read from - and off the
    /// modules those attributes apply, since a library that carries the store attribute registers
    /// it for whoever imports the library.
    /// </remarks>
    public static bool RegistersAStore(EntryPointSelector.Model applicationModel) =>
        applicationModel.ImportsAStore ||
        (applicationModel.AttributeModels?.Any(
            attribute => StoreModuleAttributes.Contains(attribute.TypeDefinition.Name)) ?? false);

    /// <summary>
    /// Whether the entry point applies a web runtime, and so is the application rather than a
    /// library some other compilation hosts.
    /// </summary>
    public static bool IsApplication(EntryPointSelector.Model applicationModel) =>
        applicationModel.AttributeModels?.Any(
            attribute => WebRuntimeModuleAttributes.Contains(attribute.TypeDefinition.Name)) ?? false;

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
        if (!IsApplication(applicationModel) || RegistersAStore(applicationModel)) {
            return;
        }

        var declaring = handlers
            .Where(DeclaresCaching)
            .Select(handler => handler.ControllerType.Name + "." + handler.HandlerMethod)
            .Concat(applicationModel.ImportedCachedHandlers)
            .Distinct()
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
