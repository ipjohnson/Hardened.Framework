using System.Text.Json.Serialization.Metadata;
using Hardened.Shared.Runtime.Collections;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;

/// <summary>
/// The production serializer and pool, not substitutes. A batch filter reads its event through
/// <see cref="IJsonSerializer"/> and writes its partial-batch response through the same instance,
/// so a stand-in that round-trips differently would hide exactly the mapping this project is here
/// to check.
/// </summary>
public static class TestJson {
    public static IJsonSerializer Serializer { get; } =
        new JsonSerializerImpl(
            Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration()),
            // No resolver is registered in these tests, and the serializer appends reflection to
            // whatever it is handed - so an empty set is the whole chain, not a missing one.
            Array.Empty<IJsonTypeInfoResolver>());

    public static IMemoryStreamPool Pool { get; } = new MemoryStreamPool();

    public static MemoryStream ToStream(object value) {
        var stream = new MemoryStream();

        Serializer.SerializeAsync(stream, value).GetAwaiter().GetResult();

        stream.Position = 0;

        return stream;
    }

    public static T FromStream<T>(Stream stream) {
        stream.Position = 0;

        return Serializer.DeserializeAsync<T>(stream).GetAwaiter().GetResult();
    }
}
