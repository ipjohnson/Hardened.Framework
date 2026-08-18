using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl;

[TransientService]
public class NewtonsoftSerializer : IResponseSerializer {
    private readonly ISharedSerializer _sharedSerializer;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public NewtonsoftSerializer(ISharedSerializer sharedSerializer, IMemoryStreamPool memoryStreamPool) {
        _sharedSerializer = sharedSerializer;
        _memoryStreamPool = memoryStreamPool;
    }

    public bool IsDefaultSerializer => true;

    /// <summary>
    /// Ahead of <c>SystemTextJsonResponseSerializer</c>, and behind the AOT serializers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Displacing the built-in JSON serializer is the entire purpose of this package, and until
    /// 2026-08-18 it did not state a precedence at all. It sat at
    /// <see cref="ResponseSerializerOrder.Normal"/> — exactly where
    /// <c>SystemTextJsonResponseSerializer</c> sits — and both return
    /// <see cref="IsDefaultSerializer"/>, so which one wrote a JSON response came down to which
    /// module registered last. Installing the package was not enough to be sure it was used.
    /// </para>
    /// <para>
    /// <b>Behind <see cref="ResponseSerializerOrder.Specialized"/> rather than at it</b>, so
    /// <c>AotResponseSerializer</c> still wins in an application that imports
    /// <c>[AotSerializerModule]</c>. That combination is contradictory — this serializer is
    /// reflection-based and the AOT module exists because reflection is not there — and if anything
    /// resolves it, the source-generated one is the answer. Slotting between two named values is
    /// what the enum's spacing is for.
    /// </para>
    /// </remarks>
    public int Order => (int)ResponseSerializerOrder.Specialized + 1;

    public bool CanProduce(string mediaType, IExecutionContext context) =>
        MediaType.Matches(mediaType, KnownContentType.Json);

    public async Task SerializeResponse(IExecutionContext context) {
        using var outputBuffer = _memoryStreamPool.Get();
        await using var textWriter = new StreamWriter(outputBuffer.Item, null, -1, true);

        _sharedSerializer.Serializer.Serialize(textWriter, context.Response.ResponseValue);

        await textWriter.FlushAsync();

        outputBuffer.Item.Position = 0;

        await outputBuffer.Item.CopyToAsync(context.Response.Body);
    }
}