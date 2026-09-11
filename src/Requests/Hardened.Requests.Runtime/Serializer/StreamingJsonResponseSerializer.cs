using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// One item of a streamed response, as JSON. The framing around it belongs to the filter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without this, a streamed response of anything but a string throws.</b>
/// <c>AsyncEnumerableIoFilter</c> commits <c>application/x-ndjson</c> before the loop, and a
/// committed content type sends <c>SerializationLocatorService</c> looking for a serializer that
/// can produce it. Nothing could: the JSON serializers answer only for
/// <see cref="KnownContentType.Json"/>, and <see cref="RawResponseSerializer"/> requires the value
/// to already be a <c>string</c>, <c>byte[]</c> or <c>Stream</c>. So every handler returning
/// <c>IAsyncEnumerable&lt;SomeModel&gt;</c> failed with "Response committed to content type
/// 'application/x-ndjson' but no registered serializer can produce it", while
/// <c>IAsyncEnumerable&lt;string&gt;</c> worked - which is the one shape the tests covered.
/// </para>
/// <para>
/// <b>It deliberately does not assign <c>ContentType</c>.</b> Every other JSON serializer here sets
/// <c>application/json</c> on entry, which is right when it is writing the whole response and wrong
/// when it is writing one item of a stream - the filter has already said what the stream is, and an
/// item does not get to restate it. For <see cref="KnownContentType.EventStream"/> that is not a
/// cosmetic difference: a browser <c>EventSource</c> rejects any other content type.
/// </para>
/// <para>
/// <b>It does not compress either</b>, and neither does any other serializer. Compression is one
/// encoder around the whole body, opened by the response compression filter outside this one, so
/// a compressed stream is a single member that the per-item flush delivers incrementally.
/// </para>
/// <para>
/// Resolution goes through <c>JsonTypeInfoLookup</c> rather than the reflection overload, so
/// one class serves both hosts: an AOT application answers out of its registered
/// <c>JsonSerializerContext</c>, and a JIT application falls back to reflection through the chain
/// <c>WithReflectionFallback</c> installs. There is no <c>Aot</c> twin of this type for that reason.
/// </para>
/// </remarks>
public class StreamingJsonResponseSerializer : IResponseSerializer {
    private readonly JsonSerializerOptions _serializerOptions;

    public StreamingJsonResponseSerializer(
        IOptions<IJsonSerializerConfiguration> configuration,
        IEnumerable<IJsonTypeInfoResolver> resolvers) {
        // Resolvers before reflection - see JsonTypeInfoLookup.WithResolvers. A streamed item was
        // written by whichever of the two answered first, so an enum in an IAsyncEnumerable<T> did
        // not agree with the same enum in a buffered response.
        _serializerOptions =
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.WithResolvers(
                configuration.Value.SerializeOptions ??
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                resolvers);

        // Last, so a registered context still answers first - see PrimitiveJsonTypeInfoResolver.
        _serializerOptions.TypeInfoResolverChain.Add(
            Hardened.Shared.Runtime.Json.PrimitiveJsonTypeInfoResolver.Instance);
    }

    /// <summary>
    /// Never the fallback. A response nothing committed to a stream media type is not this
    /// serializer's business.
    /// </summary>
    public bool IsDefaultSerializer => false;

    /// <summary>
    /// Newline-delimited JSON, which is what a streamed handler answers unless it names a framing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It writes <see cref="KnownContentType.EventStream"/> too - see <see cref="CanProduce"/> -
    /// and the tag names one of the two because the tag is how a serializer is found for a
    /// <em>declared</em> type. A streamed handler is bound to this one from its return type as its
    /// pipeline is composed, so the second media type never needs to be looked up.
    /// </para>
    /// <para>
    /// <b>It writes the item, not the envelope.</b> <c>RawResponseSerializer</c> would write a
    /// string's characters, so <c>IAsyncEnumerable&lt;string&gt;</c> produced lines reading
    /// <c>alpha</c> - not a JSON document, in a format whose entire contract is one JSON document
    /// per line. Through this serializer the same handler emits <c>"alpha"</c>, and every line
    /// parses regardless of item type.
    /// </para>
    /// </remarks>
    public string ContentType => KnownContentType.NdJson;

    /// <summary>
    /// Only for a response that has already committed to a stream content type - never for one a
    /// client asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The committed type is the question, not <paramref name="mediaType"/>.</b>
    /// <see cref="MediaType.Matches"/> answers true for <c>*/*</c> and for an absent <c>Accept</c>,
    /// on the reasoning that a client expressing no preference will take anything. Asking it about
    /// this serializer's media types therefore claims every indifferent request - which, at
    /// <c>Specialized</c>, is most of them, and is ahead of the JSON serializers. Written that way
    /// it broke plain-text fallback and the content type <c>HEAD</c> carries over from its
    /// <c>GET</c>.
    /// </para>
    /// <para>
    /// Gating on the commitment is also the honest rule. Streaming is the handler's decision, taken
    /// by returning <c>IAsyncEnumerable&lt;T&gt;</c>, and the filter records it before any item is
    /// written. There is no <c>Accept</c> a client can send that should turn a buffered handler
    /// into a streaming one.
    /// </para>
    /// </remarks>
    public bool CanProduce(string mediaType, IExecutionContext context) {
        var committed = context.Response.ContentType;

        return !string.IsNullOrEmpty(committed) &&
               (MediaType.Matches(committed, KnownContentType.NdJson) ||
                MediaType.Matches(committed, KnownContentType.EventStream));
    }

    public Task SerializeResponse(IExecutionContext context) {
        var value = context.Response.ResponseValue;

        if (value == null) {
            return Task.CompletedTask;
        }

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            value,
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.For(_serializerOptions, value));
    }
}
