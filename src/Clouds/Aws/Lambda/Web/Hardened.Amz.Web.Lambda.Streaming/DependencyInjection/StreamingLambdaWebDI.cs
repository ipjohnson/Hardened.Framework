using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Web.Lambda.Streaming.DependencyInjection;

public static class StreamingLambdaWebDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        LambdaWebDI.Register(environment, serviceCollection);

        serviceCollection.TryAddSingleton<ILambdaInvokeEngine, LambdaInvokeEngine>();
        serviceCollection.TryAddSingleton<ILambdaServerProxy, LambdaServerProxy>();
        serviceCollection.TryAddSingleton<ILambdaHttpClientProvider, LambdaHttpClientProvider>();
        serviceCollection.TryAddSingleton<IStreamingRequestMapper, StreamingRequestMapper>();

        // Replace default serialization service with streaming-aware version
        // that handles IAsyncEnumerable<T> return types as NDJSON
        serviceCollection.AddSingleton<IContextSerializationService, StreamingContextSerializationService>();
    }
}
