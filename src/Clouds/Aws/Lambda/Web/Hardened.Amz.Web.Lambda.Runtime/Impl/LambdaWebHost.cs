using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Web.Lambda.Runtime.Serializer;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Utilities;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Web.Lambda.Runtime.Impl;

/// <summary>
/// The handler the generated <c>Main</c> hands to <c>LambdaBootstrap</c>: the invocation's input
/// stream in, the response stream out.
/// </summary>
public interface ILambdaWebHost {
    Task<Stream> Invoke(Stream input, ILambdaContext context);
}

/// <summary>
/// One host for both response modes. The mode is read once, when the host is built, from
/// <see cref="ILambdaResponseModeConfiguration"/>; it decides which processor serves every
/// invocation and nothing about the pipeline changes with it.
/// </summary>
/// <remarks>
/// The raw stream shape is the one the AWS bootstrap offers for custom serializers and Native AOT.
/// The event is read straight off the input stream with the source-generated context, so there is
/// no string round trip, and the buffered response is written back the same way.
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class LambdaWebHost : ILambdaWebHost {
    private readonly IApiGatewayEventProcessor _buffered;
    private readonly IStreamingEventProcessor _streaming;
    private readonly IResponseStreamFactory _streams;
    private readonly IMemoryStreamPool _memoryStreamPool;
    private readonly LambdaResponseMode _mode;

    public LambdaWebHost(
        IApiGatewayEventProcessor buffered,
        IStreamingEventProcessor streaming,
        IResponseStreamFactory streams,
        IMemoryStreamPool memoryStreamPool,
        IOptions<ILambdaResponseModeConfiguration> mode) {
        _buffered = buffered;
        _streaming = streaming;
        _streams = streams;
        _memoryStreamPool = memoryStreamPool;
        _mode = mode.Value.Mode;
    }

    public async Task<Stream> Invoke(Stream input, ILambdaContext context) {
        var request = await JsonSerializer.DeserializeAsync(
            input, LambdaEventSerializerContext.Default.APIGatewayHttpApiV2ProxyRequest);

        if (request == null) {
            throw new InvalidOperationException(
                "The invocation payload is not an API Gateway HTTP API (payload format 2.0) event.");
        }

        if (_mode == LambdaResponseMode.Stream) {
            await _streaming.Process(request, context, _streams);

            // Ignored by the bootstrap once a stream was created, and every stream-mode response
            // creates one.
            return Stream.Null;
        }

        var response = await _buffered.Process(request, context);

        var output = new MemoryStreamPoolWrapper(_memoryStreamPool.Get());

        await JsonSerializer.SerializeAsync(
            output, response, LambdaEventSerializerContext.Default.APIGatewayHttpApiV2ProxyResponse);

        output.Position = 0;

        return output;
    }
}
