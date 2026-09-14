using System.Text;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The served OpenAPI document, with the registered routes written into it.
/// </summary>
/// <remarks>
/// <para>
/// There is no way to emit an OpenAPI operation at run time and there should not be one. Every
/// registered route's operation is fully determined at build time except its path key, so the build
/// writes the operation and this writes the path around it. No JSON is parsed, no schema is
/// written, and nothing is reflected over.
/// </para>
/// <para>
/// Once, when registration closes. The cost is one document-sized string and one compression, on a
/// path that is already doing I/O.
/// </para>
/// </remarks>
public static class RegisteredRouteDocument
{
    /// <summary>
    /// The compiled document with <paramref name="operations"/> spliced in, or null where there is
    /// nothing to splice or no document to splice into.
    /// </summary>
    /// <param name="operations">
    /// Each registered route's path and its operation object, in registration order. Two verbs at
    /// one path are one path item with two operations, which is what the grouping here is for.
    /// </param>
    public static string? Splice(
        string prefix,
        string suffix,
        IReadOnlyList<(string Path, string Operation)> operations
    )
    {
        if (prefix.Length == 0 || operations.Count == 0)
        {
            return null;
        }

        // Ordered, so the document a process serves does not depend on the order a registration
        // loop happened to run in.
        var byPath = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (path, operation) in operations)
        {
            if (operation.Length == 0)
            {
                continue;
            }

            if (!byPath.TryGetValue(path, out var written))
            {
                written = new List<string>();
                byPath.Add(path, written);
            }

            written.Add(operation);
        }

        if (byPath.Count == 0)
        {
            return null;
        }

        var document = new StringBuilder(prefix.Length + suffix.Length + 256);

        document.Append(prefix);

        // The prefix ends on the open brace of an empty paths object when the application declares
        // no route of its own, and on the close of the last path item otherwise.
        var first = prefix[prefix.Length - 1] == '{';

        foreach (var path in byPath)
        {
            if (!first)
            {
                document.Append(',');
            }

            document.Append('"').Append(Escape(path.Key)).Append("\":{");

            for (var index = 0; index < path.Value.Count; index++)
            {
                if (index > 0)
                {
                    document.Append(',');
                }

                document.Append(path.Value[index]);
            }

            document.Append('}');

            first = false;
        }

        return document.Append(suffix).ToString();
    }

    /// <summary>
    /// A path as a JSON string body.
    /// </summary>
    /// <remarks>
    /// A route template is a URL path, so the characters that need escaping are the ones a path
    /// should not contain at all. Escaped anyway: a registered path is a value the application
    /// computed, and a document that is not JSON is worse than a path that is refused.
    /// </remarks>
    private static string Escape(string path)
    {
        if (path.IndexOf('"') < 0 && path.IndexOf('\\') < 0)
        {
            return path;
        }

        return path.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
