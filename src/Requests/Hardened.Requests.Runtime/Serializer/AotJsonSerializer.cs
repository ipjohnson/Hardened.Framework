using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

public class AotJsonSerializer : IJsonSerializer {
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly JsonSerializerOptions _prettyOptions;

    public AotJsonSerializer(IOptions<Hardened.Shared.Runtime.Json.IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _serializerOptions = configuration.Value.Options;
        _prettyOptions = new JsonSerializerOptions(_serializerOptions) { WriteIndented = true };

        foreach (var resolver in resolvers) {
            _serializerOptions.TypeInfoResolverChain.Add(resolver);
            _prettyOptions.TypeInfoResolverChain.Add(resolver);
        }
    }

    public async Task<T> DeserializeAsync<T>(Stream jsonStream, CancellationToken cancellationToken = default) {
        return await JsonSerializer.DeserializeAsync(
                   jsonStream, JsonTypeInfoLookup.For<T>(_serializerOptions), cancellationToken) ??
               throw new Exception("Deserialized to null instance");
    }

    public T Deserialize<T>(string json) {
        return JsonSerializer.Deserialize(json, JsonTypeInfoLookup.For<T>(_serializerOptions)) ??
               throw new Exception("Deserialized to null instance");
    }

    public string Serialize(object obj, bool pretty) {
        var options = pretty ? _prettyOptions : _serializerOptions;

        return JsonSerializer.Serialize(obj, JsonTypeInfoLookup.For(options, obj));
    }

    public Task SerializeAsync(Stream jsonStream, object obj, bool pretty, CancellationToken cancellationToken) {
        var options = pretty ? _prettyOptions : _serializerOptions;

        return JsonSerializer.SerializeAsync(
            jsonStream, obj, JsonTypeInfoLookup.For(options, obj), cancellationToken);
    }
}
