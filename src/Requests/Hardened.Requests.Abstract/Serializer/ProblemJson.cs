using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// When a JSON serializer writes <c>application/problem+json</c> rather than
/// <c>application/json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The body is JSON either way, so the JSON serializers write both. What differs is the label, and
/// the label is a statement a client acts on: code that branches on the media type treats a
/// problem document as a failure to report and anything else as the representation it asked for.
/// So a problem is only ever a failure. A success labelled as one reads as an error to a client
/// that asked for <c>application/problem+json</c> and was handed the product.
/// </para>
/// <para>
/// Every JSON serializer calls these two, so the rule is stated once whichever of them an
/// application registered.
/// </para>
/// </remarks>
public static class ProblemJson
{
    /// <summary>
    /// Whether a JSON serializer can answer this response as <paramref name="mediaType"/> because
    /// it is <c>application/problem+json</c> and the response is a failure.
    /// </summary>
    /// <remarks>
    /// Compared exactly, parameters aside. A wildcard is answered by the serializer's own
    /// <c>application/json</c>, and a failure that should be a problem document is relabelled by
    /// <see cref="ContentTypeFor"/> as it is written.
    /// </remarks>
    public static bool Produces(string mediaType, IExecutionContext context) =>
        IsProblemJson(mediaType) && IsFailure(context);

    /// <summary>
    /// The media type a JSON serializer writes this response as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>application/problem+json</c> for a failure that negotiation already committed to it, which
    /// is a contract declaring its failures that way, and for a failure whose body is an
    /// <see cref="IProblemDetails"/>, which is every problem record Hardened ships.
    /// <c>application/json</c> for everything else, including the framework's own
    /// <c>ErrorModel</c>, which is not in RFC 9457's shape.
    /// </para>
    /// <para>
    /// Read before the first byte is written. The headers go out with it, so a label chosen after
    /// delegating to the writer is a label the client never sees.
    /// </para>
    /// </remarks>
    public static string ContentTypeFor(IExecutionContext context)
    {
        if (
            IsFailure(context)
            && (
                IsProblemJson(context.Response.ContentType)
                || context.Response.ResponseValue is IProblemDetails
            )
        )
        {
            return KnownContentType.ProblemJson;
        }

        return KnownContentType.Json;
    }

    private static bool IsFailure(IExecutionContext context) => context.Response.Status >= 400;

    private static bool IsProblemJson(string? mediaType) =>
        mediaType != null && MediaType.Matches(KnownContentType.ProblemJson, mediaType);
}
