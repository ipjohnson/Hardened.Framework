using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Web.Lambda.Streaming.Impl;
using Hardened.Requests.Abstract.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Web.Lambda.Streaming.DependencyInjection;

[DependencyModule]
[LambdaWebModule]
public partial class StreamingLambdaWebModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<ILambdaInvokeEngine, LambdaInvokeEngine>();
        services.TryAddSingleton<ILambdaServerProxy, LambdaServerProxy>();
        services.TryAddSingleton<ILambdaHttpClientProvider, LambdaHttpClientProvider>();
        services.TryAddSingleton<IStreamingRequestMapper, StreamingRequestMapper>();

        // Replace default serialization service with streaming-aware version
        // that handles IAsyncEnumerable<T> return types as NDJSON
        services.AddSingleton<IContextSerializationService, StreamingContextSerializationService>();
    }
}
