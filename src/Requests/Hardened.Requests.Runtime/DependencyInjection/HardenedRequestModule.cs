using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Interfaces;
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
using Hardened.Shared.Runtime.Configuration;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.DependencyInjection;

[DependencyModule]
[HardenedCoreModule]
public partial class HardenedRequestModule : IServiceCollectionConfiguration {
    public void ConfigureServices(IServiceCollection services) {
        services.TryAddSingleton<IMiddlewareService, MiddlewareService>();
        services.TryAddSingleton<IContextSerializationService, ContextSerializationService>();
        services.TryAddSingleton<IRequestDeserializer, SystemTextJsonRequestDeserializer>();
        services.TryAddSingleton<IResponseSerializer, SystemTextJsonResponseSerializer>();
        services.TryAddSingleton<IGlobalFilterRegistry, GlobalFilterRegistry>();
        services.TryAddSingleton<IRequestLogger, RequestLogger>();
        services.TryAddSingleton<INullValueResponseHandler, NullValueResponseHandler>();
        services.TryAddSingleton<IResourceNotFoundHandler, ResourceNotFoundHandler>();
        services.TryAddSingleton<IExceptionResponseSerializer, ExceptionResponseSerializer>();
        services.TryAddSingleton<ISerializationLocatorService, SerializationLocatorService>();
        services.TryAddSingleton<IExceptionToModelConverter, ExceptionToModelConverter>();
        services.TryAddSingleton<IIOFilterProvider, IOFilterProvider>();
        services.TryAddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        services.TryAddSingleton<IStringConverterService, StringConverterService>();
        services.TryAddSingleton<IKnownServices, KnownServices>();
        services.AddSingleton<IConfigurationPackage>(
            new SimpleConfigurationPackage(new IConfigurationValueProvider[] {
                new NewConfigurationValueProvider<IResponseHeaderConfiguration, ResponseHeaderConfiguration>(null),
                new NewConfigurationValueProvider<IJsonSerializerConfiguration, JsonSerializerConfiguration>(null)
            }));
        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IResponseHeaderConfiguration>()));

        services.AddSingleton(
            s => Options.Create(s.GetRequiredService<IConfigurationManager>()
                .GetConfiguration<IJsonSerializerConfiguration>()));
    }
}
