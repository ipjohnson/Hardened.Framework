using System.Collections.Generic;
using System.Linq;

namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// Every enum vocabulary reachable from a set of handlers.
/// </summary>
/// <remarks>
/// Beside the models rather than in Web/, because it has two consumers on different compile sets:
/// the wire-converter emitter, which only the web generator compiles, and the document writer,
/// which every generator wrapper compiles. Living in Web/ put a <c>Web.</c> reference into the
/// writer and broke the wrappers that take Models/ and OpenApiDocument/ without Web/ - a compile
/// error only those wrappers could see.
/// </remarks>
public static class EnumVocabularies {

    /// <summary>
    /// Every enum reachable from a handler's request, response or declared response set,
    /// deduplicated by qualified name.
    /// </summary>
    /// <remarks>
    /// One enum reached from two handlers resolves to the same vocabulary - it is a property of
    /// the type, not of the route.
    /// </remarks>
    public static IReadOnlyList<EnumVocabulary> Collect(IReadOnlyList<RequestHandlerModel> handlers) {
        var found = new SortedDictionary<string, EnumVocabulary>(System.StringComparer.Ordinal);

        foreach (var handler in handlers) {
            Add(found, handler.RequestSchema);
            Add(found, handler.ResponseSchema);

            foreach (var response in handler.ResponseSchemas) {
                Add(found, response.Schema);
            }
        }

        return found.Values.ToList();
    }

    private static void Add(IDictionary<string, EnumVocabulary> found, HandlerSchema? schema) {
        if (schema == null) {
            return;
        }

        foreach (var vocabulary in schema.Enums) {
            found[vocabulary.QualifiedName] = vocabulary;
        }
    }
}
