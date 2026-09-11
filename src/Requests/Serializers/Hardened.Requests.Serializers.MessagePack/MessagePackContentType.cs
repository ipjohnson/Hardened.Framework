namespace Hardened.Requests.Serializers.MessagePack;

/// <summary>
/// The media type this package reads and writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>One spelling, and this is it.</b> <c>application/msgpack</c> and
/// <c>application/vnd.msgpack</c> are both in use, and supporting more than one here would not
/// work: <c>SerializationLocatorService.ProducerOf</c> is an exact lookup on a serializer's own
/// <c>ContentType</c>, and it is the lookup the compile-time binding uses. A second spelling
/// answered through <c>CanProduce</c> alone would negotiate for an operation declaring two media
/// types and fail to bind for one declaring it alone, which is a difference nothing on the outside
/// explains.
/// </para>
/// <para>
/// Public so an operation can name it without a string literal:
/// <c>[Produces(KnownContentType.Json, MessagePackContentType.Value)]</c>.
/// </para>
/// </remarks>
public static class MessagePackContentType {
    public const string Value = "application/x-msgpack";
}
