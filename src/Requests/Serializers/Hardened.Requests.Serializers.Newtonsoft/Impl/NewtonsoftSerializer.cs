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
    /// The same tag the built-in JSON serializers declare. Displacing them is the entire purpose of
    /// this package, and it does that by registering after them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry keeps the last registration under a content type. This module is imported by
    /// the application and <c>HardenedRequestModule</c> is a dependency, so importing the package is
    /// what makes it the JSON serializer.
    /// </para>
    /// <para>
    /// <b>An application importing this and <c>[AotSerializerModule]</c> gets whichever it named
    /// last.</b> The combination is contradictory in the first place - this serializer is
    /// reflection-based and the AOT module exists because reflection is not there - and the rule
    /// resolves it the way the author wrote it rather than by a precedence they cannot see. The
    /// previous <c>Order</c> always resolved it in the AOT serializer's favour, whichever order the
    /// two were imported in.
    /// </para>
    /// </remarks>
    public string ContentType => KnownContentType.Json;

    public async Task SerializeResponse(IExecutionContext context) {
        using var outputBuffer = _memoryStreamPool.Get();
        await using var textWriter = new StreamWriter(outputBuffer.Item, null, -1, true);

        _sharedSerializer.Serializer.Serialize(textWriter, context.Response.ResponseValue);

        await textWriter.FlushAsync();

        outputBuffer.Item.Position = 0;

        await outputBuffer.Item.CopyToAsync(context.Response.Body);
    }
}