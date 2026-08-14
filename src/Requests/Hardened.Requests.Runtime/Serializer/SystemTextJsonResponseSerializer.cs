using System.IO.Compression;
using System.Text.Json;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

/// <remarks>
/// <c>Add</c>, not <c>Try</c>. <c>Try</c> was doing double duty here: registering the serializer,
/// and acting as the switch that lets <c>AotSerializerModule</c> replace it - TryAddSingleton is a
/// no-op once anything has registered <c>IResponseSerializer</c>, so an AOT application that
/// registered its serializer first got only that one.
///
/// That switch keys on the service type rather than on this class, so it fired for any registration
/// at all. Adding RawResponseSerializer - which sorts before this one, because DependencyModules
/// orders a module's registrations by implementation type name - silently unregistered JSON
/// everywhere, and every route in the benchmark fixture failed with "could not locate response
/// serializer". Nothing about that is specific to the raw serializer; the next serializer added
/// would have done the same.
///
/// AOT precedence is now stated as an order instead - see <c>AotResponseSerializer.Order</c> - which
/// does not depend on registration timing or on how two class names happen to sort.
/// </remarks>
[SingletonService(Using = RegistrationType.Add)]
public class SystemTextJsonResponseSerializer : IResponseSerializer {
    private readonly JsonSerializerOptions _serializerOptions;

    public SystemTextJsonResponseSerializer(IOptions<IJsonSerializerConfiguration> configuration) {
        _serializerOptions =
            configuration.Value.SerializeOptions ??
            new(JsonSerializerDefaults.Web);
    }

    public bool IsDefaultSerializer => true;

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
            await System.Text.Json.JsonSerializer.SerializeAsync(gzipStream, context.Response.ResponseValue,
                _serializerOptions);

            await gzipStream.FlushAsync();
        }
        else {
            await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, context.Response.ResponseValue,
                _serializerOptions);
        }
    }
}