using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Requests.Runtime;

public class AotSerializerModule : IDependencyModule {
    public void PopulateServiceCollection(IServiceCollection services) {
        services.Replace(new ServiceDescriptor(
            typeof(IResponseSerializer), typeof(AotResponseSerializer), ServiceLifetime.Singleton));
        services.Replace(new ServiceDescriptor(
            typeof(IRequestDeserializer), typeof(AotRequestDeserializer), ServiceLifetime.Singleton));
        services.Replace(new ServiceDescriptor(
            typeof(IJsonSerializer), typeof(AotJsonSerializer), ServiceLifetime.Singleton));
    }

    public override bool Equals(object? obj) => obj is AotSerializerModule;
    public override int GetHashCode() => typeof(AotSerializerModule).GetHashCode();
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AotSerializerModuleAttribute : Attribute, IDependencyModuleProvider {
    public IDependencyModule GetModule() => new AotSerializerModule();
}
