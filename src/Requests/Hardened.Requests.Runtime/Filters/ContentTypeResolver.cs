using System.Collections.Concurrent;
using System.Reflection;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

/// <summary>
/// What media types a handler produces, from the four places one can be declared.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most specific wins.</b> The operation, then its class, then the handler's own assembly, then
/// whatever the application registered as its default. Nothing is combined: the nearest declaration
/// is the answer and the rest are fallbacks.
/// </para>
/// <para>
/// The first two arrive already resolved. The generator reads a method's <c>[Produces]</c> ahead of
/// its class's and stamps the winner into the handler info, because that is where the syntax is and
/// because the OpenAPI document needs the same answer at build. This picks up the two rungs below,
/// which only exist at run time - an assembly's attributes, and a registration.
/// </para>
/// <para>
/// Asked once per handler, as its filter chain is built, so the reflection over an assembly's
/// attributes happens once per assembly and the rest is a dictionary lookup. Mirrors
/// <see cref="TimeoutResolver"/>, which resolves the same four rungs for a deadline.
/// </para>
/// </remarks>
internal static class ContentTypeResolver {

    /// <summary>
    /// One lookup per assembly rather than one per handler, since a controller with twenty routes
    /// asks the same question twenty times.
    /// </summary>
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<string>?> AssemblyDeclarations = new();

    public static IReadOnlyList<string> Resolve(
        IServiceProvider serviceProvider, IExecutionRequestHandlerInfo handlerInfo) {
        // The operation and its class, which the generator already resolved between them.
        if (handlerInfo.ProducedContentTypes.Count > 0) {
            return handlerInfo.ProducedContentTypes;
        }

        return ForAssembly(handlerInfo.HandlerType.Assembly) ??
               RegisteredDefault(serviceProvider) ??
               Array.Empty<string>();
    }

    /// <summary>
    /// <c>[assembly: Produces]</c> on the handler's assembly, or null.
    /// </summary>
    /// <remarks>
    /// The <em>handler's</em> assembly, so a declaration written beside an entry point covers that
    /// assembly's own handlers and not a referenced library's.
    /// </remarks>
    private static IReadOnlyList<string>? ForAssembly(Assembly assembly) =>
        AssemblyDeclarations.GetOrAdd(
            assembly,
            static declaring => {
                var declared = declaring.GetCustomAttributes()
                    .OfType<ProducesAttribute>()
                    .FirstOrDefault()
                    ?.ContentTypes;

                return declared is { Length: > 0 } ? declared : null;
            });

    /// <summary>
    /// The last <see cref="ResponseContentTypeDefault"/> the application registered, or null.
    /// </summary>
    private static IReadOnlyList<string>? RegisteredDefault(IServiceProvider serviceProvider) {
        // GetService rather than GetServices, for the reason ExecutionHelper.ApplyConventions
        // gives: the convenience overload resolves IEnumerable<T> as required, and Hardened's
        // container does not synthesise an empty one.
        var registered = serviceProvider.GetService<IEnumerable<ResponseContentTypeDefault>>();

        if (registered == null) {
            return null;
        }

        IReadOnlyList<string>? last = null;

        foreach (var declaration in registered) {
            if (declaration.ContentTypes.Count > 0) {
                last = declaration.ContentTypes;
            }
        }

        return last;
    }
}
