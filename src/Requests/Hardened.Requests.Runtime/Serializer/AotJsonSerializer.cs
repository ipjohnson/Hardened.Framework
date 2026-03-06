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
        using var streamReader = new StreamReader(jsonStream);

        return await JsonSerializer.DeserializeAsync<T>(jsonStream, _serializerOptions, cancellationToken) ??
               throw new Exception("Deserialized to null instance");
    }

    public T Deserialize<T>(string json) {
        return JsonSerializer.Deserialize<T>(json, _serializerOptions) ??
               throw new Exception("Deserialized to null instance");
    }

    public string Serialize(object obj, bool pretty) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            obj,
            pretty ? _prettyOptions : _serializerOptions);

        return Encoding.UTF8.GetString(bytes);
    }

    public Task SerializeAsync(Stream jsonStream, object obj, bool pretty, CancellationToken cancellationToken) {
        return JsonSerializer.SerializeAsync(
            jsonStream,
            obj,
            pretty ? _prettyOptions : _serializerOptions,
            cancellationToken);
    }
}
