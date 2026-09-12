using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using Refit;

namespace Hardened1.Client;

/// <summary>
/// Reads and writes this client's bodies as MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// The generated contracts carry [MessagePackObject] and [Key], and that on its own changes
/// nothing on the wire: Refit picks its format from RefitSettings.ContentSerializer, and the
/// default is System.Text.Json. This is what makes the attributes load-bearing.
/// </para>
/// <para>
/// Hand-written rather than referenced, because a client package should depend on Refit and
/// MessagePack and nothing else. It needs no Hardened package: every body it reads is one of its
/// own generated contracts - the payloads, and the error shapes the document declares - so
/// MessagePack's source generator has written a formatter for each of them in this project.
/// </para>
/// <para>
/// Yours to change. Swapping in LZ4 compression, or adding a resolver for a type you wrote by
/// hand, is an edit here.
/// </para>
/// </remarks>
public sealed class MessagePackContentSerializer : IHttpContentSerializer {

    /// <summary>
    /// The media type this writes, and the one the service declares.
    /// </summary>
    /// <remarks>
    /// One spelling. application/msgpack and application/vnd.msgpack are both in use, and the
    /// service resolves a serializer by an exact match on this string.
    /// </remarks>
    public const string ContentType = "application/x-msgpack";

    /// <summary>
    /// No reflection fallback, which is deliberate and is the chain the service composes too.
    /// </summary>
    /// <remarks>
    /// StandardResolver ends in DynamicObjectResolver, which emits a formatter at run time for any
    /// type that reaches it - so a contract nobody annotated works on your machine and throws
    /// PlatformNotSupportedException after a NativeAOT publish. Leaving it out means a missing
    /// formatter fails the same way everywhere.
    /// </remarks>
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                [],
                [
                    SourceGeneratedFormatterResolver.Instance,
                    BuiltinResolver.Instance,
                    AttributeFormatterResolver.Instance,
                    DynamicGenericResolver.Instance,
                    PrimitiveObjectResolver.Instance
                ]));

    public HttpContent ToHttpContent<T>(T item) {
        // The runtime type, not T. Refit hands a request body in as object, and asking for
        // Serialize<object> reaches PrimitiveObjectResolver, which refuses anything that is not a
        // primitive - "Not supported primitive object resolver. type:NewTodo".
        var content = new ByteArrayContent(
            item is null
                ? MessagePackSerializer.Serialize(item, Options)
                : MessagePackSerializer.Serialize(item.GetType(), item, Options));

        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        return content;
    }

    public async Task<T?> FromHttpContentAsync<T>(
        HttpContent content, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(content);

        // An empty body is not a MessagePack nil, and asking the deserializer to read one throws
        // rather than answering null. A 204 and a 404 with no body both arrive this way.
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        if (bytes.Length == 0) {
            return default;
        }

        return LooksLikeJson(bytes)
            ? JsonSerializer.Deserialize<T>(bytes, Json)
            : MessagePackSerializer.Deserialize<T>(bytes, Options, cancellationToken);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Whether the body is JSON rather than MessagePack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A service on [ErrorBodies(ErrorBodyFormat.Json)] answers its failures as JSON while its
    /// successes stay MessagePack, so one client reads both. That is the setting to reach for with
    /// Refit: its ApiException carries the response content as a string, and a binary error body
    /// does not survive being decoded and re-encoded.
    /// </para>
    /// <para>
    /// Sniffed rather than read off the content type, and that is Refit's doing. GetContentAsAsync
    /// wraps the string in a fresh StringContent before handing it here, so the real media type is
    /// gone by then - it is still on ApiException.ContentHeaders, where a content serializer cannot
    /// see it. On the success path the header is intact and this answers the same thing anyway.
    /// </para>
    /// <para>
    /// Safe for the bodies in play, which are objects. A MessagePack map begins 0x80-0x8F, 0xDE or
    /// 0xDF and an array 0x90-0x9F, 0xDC or 0xDD; none of them is '{' or '['. Leading whitespace is
    /// skipped because a formatted body is still JSON.
    /// </para>
    /// </remarks>
    private static bool LooksLikeJson(byte[] bytes) {
        foreach (var b in bytes) {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
                continue;
            }

            return b is (byte)'{' or (byte)'[';
        }

        return false;
    }

    /// <summary>
    /// The name a property carries on the wire, for the query and form binding Refit does itself.
    /// </summary>
    /// <remarks>
    /// Null, which means "no opinion" and leaves Refit its own convention. A [Key] answers a
    /// question about a body, and this method is asked about parameters that never reach one.
    /// </remarks>
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo) => null;
}
