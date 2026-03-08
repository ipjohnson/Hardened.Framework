using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft.Impl;

public interface ISharedSerializer {
    JsonSerializer Serializer { get; }
}

[SingletonService]
public class SharedSerializer : ISharedSerializer {
    public SharedSerializer(IServiceProvider serviceProvider,
        IOptions<INewtonsoftSerializerConfiguration> configuration) {
        Serializer = configuration.Value.SerializerProvider(serviceProvider);
    }

    public JsonSerializer Serializer { get; }
}