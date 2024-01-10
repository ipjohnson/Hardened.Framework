using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.DependencyInjection;

public class RequestRuntimeDI {
    public static void Register(IEnvironment environment, IServiceCollection serviceCollection) {
        if (DependencyRegistry<RequestRuntimeDI>.ShouldRegisterModule(serviceCollection)) {
            serviceCollection.TryAddSingleton<IMiddlewareService, MiddlewareService>();
            serviceCollection.TryAddSingleton<IContextSerializationService, ContextSerializationService>();
            serviceCollection.TryAddSingleton<IRequestDeserializer, SystemTextJsonRequestDeserializer>();
            serviceCollection.TryAddSingleton<IResponseSerializer, SystemTextJsonResponseSerializer>();
            serviceCollection.TryAddSingleton<IGlobalFilterRegistry, GlobalFilterRegistry>();
            serviceCollection.TryAddSingleton<IRequestLogger, RequestLogger>();
            serviceCollection.TryAddSingleton<INullValueResponseHandler, NullValueResponseHandler>();
            serviceCollection.TryAddSingleton<IResourceNotFoundHandler, ResourceNotFoundHandler>();
            serviceCollection.TryAddSingleton<IExceptionResponseSerializer, ExceptionResponseSerializer>();
            serviceCollection.TryAddSingleton<ISerializationLocatorService, SerializationLocatorService>();
            serviceCollection.TryAddSingleton<IExceptionToModelConverter, ExceptionToModelConverter>();
            serviceCollection.TryAddSingleton<IIOFilterProvider, IOFilterProvider>();
            serviceCollection.TryAddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
            serviceCollection.TryAddSingleton<IStringConverterService, StringConverterService>();
            serviceCollection.TryAddSingleton<IKnownServices, KnownServices>();
            serviceCollection.AddSingleton<IConfigurationPackage>(
                new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                    new NewConfigurationValueProvider<IResponseHeaderConfiguration, ResponseHeaderConfiguration>(null),
                    new NewConfigurationValueProvider<IJsonSerializerConfiguration, JsonSerializerConfiguration>(null)
                }));
            serviceCollection.AddSingleton(
                s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IResponseHeaderConfiguration>()));

            serviceCollection.AddSingleton(
                s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                    .GetConfiguration<IJsonSerializerConfiguration>()));
        }
    }
}