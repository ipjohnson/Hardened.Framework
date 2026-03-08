using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Function.Lambda.Streaming.DependencyInjection;

[DependencyModule]
[LambdaFunctionModule]
public partial class StreamingLambdaFunctionModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<IFunctionInvokeEngine, FunctionInvokeEngine>();
        services.TryAddSingleton<IFunctionServerProxy, FunctionServerProxy>();
        services.TryAddSingleton<ILambdaHttpClientProvider, LambdaHttpClientProvider>();
        services.TryAddSingleton<IStreamingFunctionRequestMapper, StreamingFunctionRequestMapper>();

        // Replace default serialization service with streaming-aware version
        // that handles IAsyncEnumerable<T> return types as NDJSON
        services.AddSingleton<IContextSerializationService, StreamingContextSerializationService>();
    }
}
