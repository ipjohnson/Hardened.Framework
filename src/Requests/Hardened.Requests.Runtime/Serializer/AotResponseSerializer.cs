using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

public class AotResponseSerializer : IResponseSerializer {
    private readonly JsonSerializerOptions _serializerOptions;

    public AotResponseSerializer(IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        // No reflection resolver, on any host. This used to install one while building the options,
        // which did two things wrong at once: it sat at the head of the chain, where it answered for
        // nearly every type and the resolvers added below were never reached; and it meant the class
        // named for AOT resolved by reflection whenever reflection happened to be available.
        //
        // The second is the one that matters. A type missing from every registered context is a
        // NotSupportedException after publishing, and reflecting over it on a JIT host turns that
        // into a defect the tests cannot see - the developer's build is the one configuration where
        // the mistake is invisible. Matching AotRequestDeserializer, which has always built its
        // options this way and says so.
        //
        // StreamingJsonResponseSerializer deliberately does not do this: it has no Aot twin and
        // serves both hosts from one class, so it keeps a reflection tail that the trimmer removes.
        _serializerOptions = configuration.Value.SerializeOptions ??
                             new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var resolver in resolvers) {
            _serializerOptions.TypeInfoResolverChain.Add(resolver);
        }

        // Last, so a registered context still answers first - see PrimitiveJsonTypeInfoResolver.
        _serializerOptions.TypeInfoResolverChain.Add(
            Hardened.Shared.Runtime.Json.PrimitiveJsonTypeInfoResolver.Instance);
    }

    public bool IsDefaultSerializer => true;

    /// <summary>
    /// Ahead of <see cref="SystemTextJsonResponseSerializer"/>, which is how an AOT application ends
    /// up using its own serializer rather than the reflection-based one.
    /// </summary>
    /// <remarks>
    /// This used to be arranged by registration order and TryAddSingleton: AotSerializerModule
    /// registered first and the reflection serializer's Try became a no-op. That worked only while
    /// nothing else registered an IResponseSerializer first, which stopped being true the moment one
    /// was added. Both are registered now and this one wins by order, which no third serializer can
    /// disturb.
    /// </remarks>
    public int Order => (int)ResponseSerializerOrder.Specialized;

    public bool CanProduce(string mediaType, IExecutionContext context) =>
        MediaType.Matches(mediaType, KnownContentType.Json);

    public async Task SerializeResponse(IExecutionContext context) {
        context.Response.ContentType = "application/json";

        if (context.Response.ResponseValue == null) {
            return;
        }

        if (context.Response.ShouldCompress) {
            await using var gzipStream = new GZipStream(context.Response.Body, CompressionLevel.Fastest, true);

            // Serialize into the gzip stream, not the response body underneath it - writing
            // to the body directly leaves the payload uncompressed while a GZipStream is
            // open over it.
            await System.Text.Json.JsonSerializer.SerializeAsync(
                gzipStream,
                context.Response.ResponseValue,
                Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.For(_serializerOptions, context.Response.ResponseValue));

            await gzipStream.FlushAsync();
        }
        else {
            await System.Text.Json.JsonSerializer.SerializeAsync(
                context.Response.Body,
                context.Response.ResponseValue,
                Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.For(_serializerOptions, context.Response.ResponseValue));
        }
    }
}
