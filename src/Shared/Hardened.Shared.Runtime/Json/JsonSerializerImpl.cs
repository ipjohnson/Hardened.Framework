using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization.Metadata;
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

[SingletonService(Using = RegistrationType.Try)]
public class JsonSerializerImpl : IJsonSerializer {
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly JsonSerializerOptions _prettyOptions;

    /// <param name="resolvers">
    /// Every registered <c>IJsonTypeInfoResolver</c>, ahead of reflection.
    /// </param>
    /// <remarks>
    /// This is the serializer the test host writes a request body with, so a resolver it does not
    /// see is one the harness does not honour while the application does - the harness then posts a
    /// body its own application answers 400 to, and the test reads as a defect in the handler.
    /// </remarks>
    public JsonSerializerImpl(
        IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        // Resolvers first, then reflection - and appended rather than installed only onto an empty
        // chain, because this is the reflection-based serializer and reflection belongs in its
        // chain whatever else is registered.
        _serializerOptions = JsonTypeInfoLookup.WithResolvers(configuration.Value.Options, resolvers);
        _prettyOptions = new JsonSerializerOptions(_serializerOptions) { WriteIndented = true };
    }

    /// <summary>
    /// Reads <paramref name="jsonStream"/> without taking ownership of it — the caller closes what
    /// the caller opened.
    ///
    /// <para>
    /// This used to open a <see cref="StreamReader"/> over the stream in a <c>using</c> and then
    /// never read from it: deserialization goes to <paramref name="jsonStream"/> directly. The
    /// reader's only effect was its disposal, and the default <see cref="StreamReader"/> constructor
    /// is <c>leaveOpen: false</c>, so every call closed a stream it did not own. A caller that
    /// pooled or reused the stream got an <see cref="ObjectDisposedException"/> afterwards —
    /// <c>MemoryStreamPool</c> resets <c>Position</c> when a reservation is returned, which throws
    /// on a closed stream. Found 2026-08-12 through the SQS integration harness, where the batch
    /// filter deserializes the request body and the caller then returns that body to the pool.
    /// </para>
    /// </summary>
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
            jsonStream,
            obj,
            JsonTypeInfoLookup.For(options, obj),
            cancellationToken);
    }
}