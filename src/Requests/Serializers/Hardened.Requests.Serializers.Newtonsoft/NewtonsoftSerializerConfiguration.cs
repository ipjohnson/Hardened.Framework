using Hardened.Shared.Runtime.Attributes;
using Newtonsoft.Json;

namespace Hardened.Requests.Serializers.Newtonsoft;

[ConfigurationModel]
public partial class NewtonsoftSerializerConfiguration {
    private Func<IServiceProvider, JsonSerializer> _serializerProvider = DefaultSerializer();

    private static Func<IServiceProvider, JsonSerializer> DefaultSerializer() {
        return _ => JsonSerializer.CreateDefault();
    }
}