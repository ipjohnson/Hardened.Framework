using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Requests.Runtime.DependencyInjection
{
    public static class RequestRuntimeDI
    {
        public static void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IMiddlewareService, MiddlewareService>();
            serviceCollection.TryAddSingleton<IContextSerializationService, ContextSerializationService>();
            serviceCollection.TryAddSingleton<IRequestDeserializer, SystemTextJsonRequestDeserializer>();
            serviceCollection.TryAddSingleton<IResponseSerializer, SystemTextJsonResponseSerializer>();
            serviceCollection.TryAddSingleton<IGlobalFilterRegistry, GlobalFilterRegistry>();
        }
    }
}
