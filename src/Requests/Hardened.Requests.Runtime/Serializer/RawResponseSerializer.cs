using System.Text;
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
/// <b>It never volunteers.</b> The response has to have committed to a content type - through
/// <c>[Produces]</c>, or a handler that set one - before this writes anything. It then writes any of
/// the three shapes as whatever was asked for, because the point of saying "this is a PDF" is that
/// the bytes go out unchanged.
/// </para>
/// <para>
/// It used to volunteer a bare <c>string</c> as <c>text/plain</c> for a client that asked for it,
/// sitting behind JSON so that a client expressing no preference still got JSON. That is what an
/// operation declaring nothing now means outright: nothing declared is the service default, and the
/// service default is JSON. A handler that wants text says <c>[Produces("text/plain")]</c>, which is
/// also what takes it off the negotiated path entirely.
/// </para>
/// </remarks>
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
    /// What an operation declaring nothing but a raw return type produces, and the tag this
    /// registers under.
    /// </summary>
    /// <remarks>
    /// It writes any committed type, so the tag understates it. That is deliberate: a serializer is
    /// located by tag for a declared type, and <c>text/plain</c> is the one type this can be asked
    /// for by name. Every other type reaches it through the response value's shape - see
    /// <see cref="CanProduce"/> - which is what <c>[Produces("application/pdf")]</c> on a method
    /// returning <c>byte[]</c> resolves to.
    /// </remarks>
    public string ContentType => DefaultContentType;

    /// <summary>
    /// Only for a response whose content type is already decided, and only when the value is already
    /// bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decided rather than requested, which is the question this answers.</b>
    /// <see cref="MediaType.Matches"/> is true for <c>*/*</c> and for an absent <c>Accept</c>, so
    /// asking it about <c>text/plain</c> alone would claim every indifferent request that returned a
    /// string. That is what this did when it was ordered behind JSON to suppress exactly that.
    /// </para>
    /// <para>
    /// Two things decide it, and they are the same statement made at different times. A committed
    /// <c>Response.ContentType</c> is a handler, or the streaming filter, saying what this
    /// particular response is. A declared <c>ProducedContentTypes</c> is the operation saying what
    /// every response is, and it is not on the response yet because the locator writes it once it
    /// knows something can produce it.
    /// </para>
    /// <para>
    /// An operation that declares neither gets nothing from this serializer. That is what makes a
    /// bare <c>string</c> answer JSON: nothing declared is the service default, and the service
    /// default is JSON.
    /// </para>
    /// </remarks>
    public bool CanProduce(string mediaType, IExecutionContext context) {
        if (context.Response.ResponseValue is not (string or byte[] or Stream)) {
            return false;
        }

        var committed = context.Response.ContentType;

        if (!string.IsNullOrEmpty(committed)) {
            return MediaType.Matches(mediaType, committed);
        }

        var declared = context.HandlerInfo?.ProducedContentTypes;

        if (declared == null) {
            return false;
        }

        for (var i = 0; i < declared.Count; i++) {
            if (MediaType.Matches(mediaType, declared[i])) {
                return true;
            }
        }

        return false;
    }

    public async Task SerializeResponse(IExecutionContext context) {
        var value = context.Response.ResponseValue;

        // Only when nothing has been committed. Checked for empty as well as null because the
        // ASP.NET Core host coerces a null assignment to "".
        //
        // Bytes are not text, so they do not fall back to text/plain. Reaching here at all means an
        // operation returning byte[] or Stream declared no content type, which the build refuses -
        // see HRDR001 - so this is what a handler assembled by hand answers with.
        if (string.IsNullOrEmpty(context.Response.ContentType)) {
            context.Response.ContentType =
                value is string ? DefaultContentType : "application/octet-stream";
        }

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
