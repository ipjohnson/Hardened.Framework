using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Collections;
using MessagePack;

namespace Hardened.Requests.Serializers.MessagePack.Impl;

/// <summary>
/// Reads a MessagePack request body.
/// </summary>
/// <remarks>
/// <para>
/// <b>No attribute declares this.</b> A deserializer is chosen by the inbound <c>Content-Type</c>
/// against <see cref="CanProcessContext"/>, so an operation reads a MessagePack body because the
/// client sent one, whatever the operation says it produces. <c>[Produces]</c> is a statement about
/// the response only.
/// </para>
/// </remarks>
[TransientService]
public class MessagePackRequestDeserializer : IRequestDeserializer {
    private readonly ISharedMessagePackOptions _options;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public MessagePackRequestDeserializer(
        ISharedMessagePackOptions options, IMemoryStreamPool memoryStreamPool) {
        _options = options;
        _memoryStreamPool = memoryStreamPool;
    }

    /// <summary>
    /// Never the fallback. A body with no content type, or one nothing claimed, is not MessagePack:
    /// it is a JSON body from a client that did not say so, which is what the default reader is for.
    /// </summary>
    public bool IsDefaultSerializer => false;

    /// <summary>
    /// Ahead of the JSON readers, which is where a deserializer for one specific content type
    /// belongs.
    /// </summary>
    /// <remarks>
    /// It could sit anywhere ahead of a reader that claims the same type, and nothing else claims
    /// this one. Stated rather than left at <c>Normal</c> so that adding a second MessagePack
    /// reader later is a decision about order rather than about how two class names sort.
    /// </remarks>
    public int Order => (int)RequestDeserializerOrder.Specialized;

    public bool CanProcessContext(IExecutionContext context) =>
        context.Request.ContentType?.Contains(MessagePackContentType.Value) ?? false;

    /// <summary>
    /// Reads the body as it is. A compressed body was decoded by <c>RequestDecompressionFilter</c>
    /// before the bind, which is why this does not look at <c>Content-Encoding</c>.
    /// </summary>
    /// <remarks>
    /// Copied into a pooled buffer first, because <c>MessagePackSerializer.Deserialize</c> over a
    /// stream reads it as a sequence and a host request body is forward-only and unbuffered.
    /// <c>leaveOpen</c> does not arise: nothing wraps the buffer in a reader that would close it,
    /// which is the trap <c>NewtonsoftDeserializer</c> records.
    /// </remarks>
    public async ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) {
        using var buffer = _memoryStreamPool.Get();

        await context.Request.Body.CopyToAsync(buffer.Item);

        buffer.Item.Position = 0;

        return MessagePackSerializer.Deserialize<T>(
            buffer.Item, _options.Options, context.CancellationToken);
    }
}
