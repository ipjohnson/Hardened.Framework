using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime;

public class AotSerializerModule : IDependencyModule {
    public void PopulateServiceCollection(IServiceCollection services) {
        // Register AOT serializers first; RequestRuntimeDI uses TryAddSingleton
        // so its reflection-based serializers will be skipped (last in wins).
        services.AddSingleton<IResponseSerializer, AotResponseSerializer>();
        services.AddSingleton<IRequestDeserializer, AotRequestDeserializer>();
        services.AddSingleton<IJsonSerializer, AotJsonSerializer>();
    }

    public override bool Equals(object? obj) => obj is AotSerializerModule;
    public override int GetHashCode() => typeof(AotSerializerModule).GetHashCode();
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AotSerializerModuleAttribute : Attribute, IDependencyModuleProvider {
    public IDependencyModule GetModule() => new AotSerializerModule();
}
