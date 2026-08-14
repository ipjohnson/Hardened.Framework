using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// Writes a response value that is already bytes - a string, a <c>byte[]</c> or a <c>Stream</c> -
/// without structuring it.
/// </summary>
/// <remarks>
/// <para>
/// This replaced the <c>DefaultOutput</c> closure that <c>[RawResponse]</c> used to install. That
/// closure was checked ahead of every serializer, which made it a second mechanism racing the
/// locator to claim a response - and the collision was not theoretical: a templated operation that
/// also carried a raw content type had its model handed to the raw writer, which throws on anything
/// that is not string, byte[] or Stream. Two copies of a special case existed to suppress it. As a
/// serializer it takes its turn like everything else, and both copies are gone.
/// </para>
/// <para>
/// It answers two different questions depending on whether the response has already committed to a
/// content type. Committed - <c>[RawResponse]</c>, or a handler that set one - it writes any of the
/// three shapes as whatever was asked for, because the point of saying "this is a PDF" is that the
/// bytes go out unchanged. Uncommitted, it volunteers only a string, and only for
/// <c>text/plain</c>: a string is the one value with an obvious text reading, while a
/// <c>byte[]</c> has no media type anyone could guess at.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Add)]
public class RawResponseSerializer : IResponseSerializer {
    /// <summary>What a bare string is, absent any other instruction.</summary>
    public const string DefaultContentType = "text/plain";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Never the fallback. A response value that is not already bytes is not this serializer's
    /// business, and the JSON serializer answers for everything else.
    /// </summary>
    public bool IsDefaultSerializer => false;

    /// <summary>
    /// Behind JSON, so this answers only a client that asked for text and never one that expressed
    /// no preference.
    /// </summary>
    /// <remarks>
    /// Ordering matters in exactly one case - <c>*/*</c> or no <c>Accept</c> header, where both this
    /// and JSON qualify for a string. Ahead of JSON, every handler returning a bare string would
    /// start answering <c>text/plain</c> instead of a quoted JSON string. ASP.NET Core works that
    /// way, and it was the first thing tried here, but it is not what this framework already does:
    /// sixteen of the thirty-four tests in the hand-written web fixture changed, none of them about
    /// content types - they are routing, verb and parameter-binding tests whose controllers happen
    /// to return strings.
    ///
    /// Behind JSON, nothing about an indifferent client changes and a client that does ask for
    /// text/plain still gets it, which is all TechEmpower's plaintext test needs. A handler that
    /// wants text regardless says so with [RawResponse].
    /// </remarks>
    public int Order => (int)ResponseSerializerOrder.Deferred;

    public bool CanProduce(string mediaType, IExecutionContext context) {
        var value = context.Response.ResponseValue;

        if (value is not (string or byte[] or Stream)) {
            return false;
        }

        var committed = context.Response.ContentType;

        if (!string.IsNullOrEmpty(committed)) {
            return MediaType.Matches(mediaType, committed);
        }

        return value is string && MediaType.Matches(mediaType, DefaultContentType);
    }

    public async Task SerializeResponse(IExecutionContext context) {
        // Only when nothing has been committed. Checked for empty as well as null because the
        // ASP.NET Core host coerces a null assignment to "".
        if (string.IsNullOrEmpty(context.Response.ContentType)) {
            context.Response.ContentType = DefaultContentType;
        }

        var value = context.Response.ResponseValue;

        switch (value) {
            case string text:
                var bytes = Utf8NoBom.GetBytes(text);

                await context.Response.Body.WriteAsync(bytes, 0, bytes.Length, context.CancellationToken);

                break;
            case byte[] raw:
                await context.Response.Body.WriteAsync(raw, 0, raw.Length, context.CancellationToken);

                break;
            case Stream stream:
                await stream.CopyToAsync(context.Response.Body, context.CancellationToken);

                break;
            default:
                // Unreachable through the locator, which only reaches here after CanProduce agreed.
                throw new InvalidOperationException(
                    $"RawResponseSerializer cannot write {value?.GetType().Name ?? "null"}; " +
                    "it handles string, byte[] and Stream.");
        }
    }
}
