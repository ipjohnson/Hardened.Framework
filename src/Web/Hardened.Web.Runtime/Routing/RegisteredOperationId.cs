using System.Text;

namespace Hardened.Web.Runtime.Routing;

/// <summary>
/// The <c>operationId</c> a registered route publishes, from the verb and the path it registered
/// at.
/// </summary>
/// <remarks>
/// <para>
/// Written here rather than at build time because one registration serves every path it is
/// registered at, and the id has to be unique across the document. A registration in a loop over
/// eleven sites is eleven operations, so eleven build-time copies of one id is not a naming choice
/// that can be made better - it is one the build cannot make at all. The same is true of a
/// declared handler registered at a second path, which used to copy its operation and its id
/// verbatim.
/// </para>
/// <para>
/// Verb and path together are unique by construction: two routes cannot register the same verb at
/// the same path, because the table refuses the second. So no counter and no deduplication pass is
/// needed, and the id a route publishes does not depend on what else registered or in what order.
/// </para>
/// <para>
/// The verb goes last, which is what an OpenAPI client generator produces from a path anyway -
/// <c>NameGet</c>, <c>DraftPost</c> - so the generated method reads the same whether the route was
/// declared or registered.
/// </para>
/// </remarks>
public static class RegisteredOperationId
{
    /// <summary>
    /// <c>GET /api/north-yard/readings/{id}</c> becomes <c>apiNorthYardReadingsByIdGet</c>.
    /// </summary>
    /// <remarks>
    /// A token contributes <c>By</c> and its name, which is how a path reads out loud and how the
    /// client generators already name a method that takes one. Anything that is not a letter or a
    /// digit separates words and is dropped, so a hyphenated segment, a token's constraint and the
    /// braces around it all disappear into the casing.
    /// </remarks>
    public static string For(string method, string path)
    {
        var builder = new StringBuilder(path.Length + 8);
        var start = true;

        foreach (var segment in Segments(path))
        {
            var token = segment.Length > 1 && segment[0] == '{';

            if (token)
            {
                Append(builder, "by", ref start);
            }

            Append(builder, token ? Name(segment) : segment, ref start);
        }

        // Lower-cased first, because the verb arrives as DELETE and Append keeps the casing a
        // word was written with - which is right for a path segment and would put a shout in the
        // middle of an identifier here.
        Append(builder, method.ToLowerInvariant(), ref start);

        return builder.ToString();
    }

    private static IEnumerable<string> Segments(string path)
    {
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length > 0)
            {
                yield return segment;
            }
        }
    }

    /// <summary>A token's name, without its braces, its catch-all star or its constraint.</summary>
    private static string Name(string segment)
    {
        var close = segment.IndexOf('}');
        var inner = segment.Substring(1, (close < 0 ? segment.Length : close) - 1);

        if (inner.Length > 0 && inner[0] == '*')
        {
            inner = inner.Substring(1);
        }

        var constraint = inner.IndexOf(':');

        return constraint < 0 ? inner : inner.Substring(0, constraint);
    }

    /// <summary>
    /// One word, camel-cased into what is already there. Runs of characters a C# identifier cannot
    /// hold end a word rather than appearing in one, so <c>north-yard</c> is <c>northYard</c>.
    /// </summary>
    /// <remarks>
    /// Only the first letter of each word is touched. A segment already written in camel case
    /// keeps the casing its author gave it, which lower-casing the remainder would have taken off
    /// a path spelled <c>/northYard</c>.
    /// </remarks>
    private static void Append(StringBuilder builder, string word, ref bool start)
    {
        var boundary = true;

        foreach (var character in word)
        {
            if (!char.IsLetterOrDigit(character))
            {
                boundary = true;

                continue;
            }

            if (start)
            {
                builder.Append(char.ToLowerInvariant(character));
                start = false;
            }
            else if (boundary)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else
            {
                builder.Append(character);
            }

            boundary = false;
        }
    }
}
