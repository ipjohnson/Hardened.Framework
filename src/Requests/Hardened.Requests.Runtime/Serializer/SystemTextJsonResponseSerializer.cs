using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
///
/// Annotated rather than fixed, because reflection is what this type is. It reads a model's shape
/// at run time, which is what an application wants until it publishes trimmed or AOT — at which
/// point the model it was reading may not be there any more. The annotations put that on the
/// record at the point of use rather than inside the build: an application importing
/// <c>AotSerializerModule</c> never reaches this code, because <c>AotResponseSerializer</c> sorts
/// ahead of it, and one that does not is told what it is relying on.
/// </remarks>
[RequiresUnreferencedCode(Reason)]
[RequiresDynamicCode(Reason)]
public class SystemTextJsonResponseSerializer : IResponseSerializer {

    private const string Reason =
        "Reads the model's shape by reflection. Import AotSerializerModule for a trimmed or " +
        "AOT-published application, which registers the source-generated serializers instead.";

    private readonly JsonSerializerOptions _serializerOptions;

    /// <param name="resolvers">
    /// Every <c>IJsonTypeInfoResolver</c> the application registered, ahead of reflection.
    /// </param>
    /// <remarks>
    /// This class used to take the configuration alone, which made
    /// <c>services.AddSingleton&lt;IJsonTypeInfoResolver&gt;(MyContext.Default)</c> - the way every
    /// other serializer here is configured, and the only AOT-safe way to say how an enum is written
    /// - do nothing at all on a JIT host. Nothing reported it: the registration succeeded, the
    /// context was simply never asked.
    /// </remarks>
    public SystemTextJsonResponseSerializer(IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        _serializerOptions =
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.WithResolvers(
                configuration.Value.SerializeOptions ??
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                resolvers);
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