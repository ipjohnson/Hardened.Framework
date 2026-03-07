using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Function.Lambda.Streaming.DependencyInjection;

public static class StreamingLambdaFunctionDI {
    public static void Register(IHardenedEnvironment environment, IServiceCollection serviceCollection) {
        LambdaFunctionDI.Register(environment, serviceCollection);

        serviceCollection.TryAddSingleton<IFunctionInvokeEngine, FunctionInvokeEngine>();
        serviceCollection.TryAddSingleton<IFunctionServerProxy, FunctionServerProxy>();
        serviceCollection.TryAddSingleton<ILambdaHttpClientProvider, LambdaHttpClientProvider>();
        serviceCollection.TryAddSingleton<IStreamingFunctionRequestMapper, StreamingFunctionRequestMapper>();

        // Replace default serialization service with streaming-aware version
        // that handles IAsyncEnumerable<T> return types as NDJSON
        serviceCollection.AddSingleton<IContextSerializationService, StreamingContextSerializationService>();
    }
}
