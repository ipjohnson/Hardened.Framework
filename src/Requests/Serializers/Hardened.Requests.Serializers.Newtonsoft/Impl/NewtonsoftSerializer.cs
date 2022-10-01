using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Collections;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl;

[Expose]
public class NewtonsoftSerializer : IResponseSerializer
{
    private readonly ISharedSerializer _sharedSerializer;
    private readonly IMemoryStreamPool _memoryStreamPool;

    public NewtonsoftSerializer(ISharedSerializer sharedSerializer, IMemoryStreamPool memoryStreamPool)
    {
        _sharedSerializer = sharedSerializer;
        _memoryStreamPool = memoryStreamPool;
    }

    public bool IsDefaultSerializer => true;

    public bool CanProcessContext(IExecutionContext context)
    {
        return context.Request.Accept?.Contains("application/json") ?? false;
    }

    public async Task SerializeResponse(IExecutionContext context)
    {
        using var outputBuffer = _memoryStreamPool.Get();
        await using var textWriter = new StreamWriter(outputBuffer.Item,null, -1, true);

        _sharedSerializer.Serializer.Serialize(textWriter, context.Response.ResponseValue);

        await textWriter.FlushAsync();

        outputBuffer.Item.Position = 0;

        await outputBuffer.Item.CopyToAsync(context.Response.Body);
    }
}