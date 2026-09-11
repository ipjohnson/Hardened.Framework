using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Collections;
using MessagePack;

namespace Hardened.Requests.Serializers.MessagePack.Impl;

/// <summary>
/// Writes a response as MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the default serializer.</b> It answers only where an operation declares
/// <see cref="MessagePackContentType.Value"/>, which is what lets the package be installed by a
/// service whose other operations answer JSON. <c>NewtonsoftSerializer</c> is the other shape: it
/// declares <c>application/json</c> and exists to replace what is registered under it.
/// </para>
/// </remarks>
[TransientService]
public class MessagePackResponseSerializer : IResponseSerializer {
    private readonly ISharedMessagePackOptions _options;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public MessagePackResponseSerializer(
        ISharedMessagePackOptions options, IMemoryStreamPool memoryStreamPool) {
        _options = options;
        _memoryStreamPool = memoryStreamPool;
    }

    public bool IsDefaultSerializer => false;

    public string ContentType => MessagePackContentType.Value;

    /// <summary>
    /// Serializes the response value, committing the content type first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The assignment is load-bearing.</b> A serializer bound at composition - which is every
    /// operation declaring one media type under <c>Lenient</c> - is called by
    /// <c>ContextSerializationService</c> directly, and nothing on that path assigns
    /// <c>Response.ContentType</c>. Only the negotiated path assigns it, in
    /// <c>SerializationLocatorService.FindDeclaredProducer</c>. Both JSON serializers set it here
    /// for the same reason.
    /// </para>
    /// <para>
    /// Through a pooled buffer rather than straight at the body, because
    /// <c>MessagePackSerializer.SerializeAsync</c> writes in several passes and a host that
    /// flushes on first write would commit the response before the length header is known. It is
    /// also what <c>ConditionalResponseStream</c> needs to compute an ETag over a complete body.
    /// </para>
    /// </remarks>
    public async Task SerializeResponse(IExecutionContext context) {
        context.Response.ContentType = MessagePackContentType.Value;

        if (context.Response.ResponseValue == null) {
            return;
        }

        using var buffer = _memoryStreamPool.Get();

        // The runtime type, because the pipeline holds the value as object and a formatter for
        // object writes a type-keyed map rather than the model. The JSON side has the identical
        // problem and solves it the identical way, in JsonTypeInfoLookup.For(options, value).
        MessagePackSerializer.Serialize(
            context.Response.ResponseValue.GetType(),
            buffer.Item,
            context.Response.ResponseValue,
            _options.Options,
            context.CancellationToken);

        buffer.Item.Position = 0;

        await buffer.Item.CopyToAsync(context.Response.Body, 81920, context.CancellationToken);
    }
}
