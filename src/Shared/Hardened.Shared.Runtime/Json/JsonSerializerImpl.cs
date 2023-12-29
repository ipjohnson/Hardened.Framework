using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Json;

public interface IJsonSerializer {
    Task<T> DeserializeAsync<T>(Stream jsonStream, CancellationToken cancellationToken = default);

    T Deserialize<T>(string json);

    string Serialize(object obj, bool pretty = false);

    Task SerializeAsync(Stream jsonStream, object obj, bool pretty = false,
        CancellationToken cancellationToken = default);
}

public class JsonSerializerImpl : IJsonSerializer {
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly JsonSerializerOptions _prettyOptions;

    public JsonSerializerImpl(IOptions<IJsonSerializerConfiguration> configuration) {
        _serializerOptions = configuration.Value.Options;
        _prettyOptions = new JsonSerializerOptions(_serializerOptions) { WriteIndented = true };
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