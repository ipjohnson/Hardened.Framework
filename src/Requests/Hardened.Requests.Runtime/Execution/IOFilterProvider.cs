using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Streaming;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Execution;

[SingletonService(Using = RegistrationType.Try)]
public class IOFilterProvider : IIOFilterProvider {
    private readonly IContextSerializationService _contextSerializationService;
    private readonly ISerializationLocatorService? _serializationLocatorService;
    private readonly RawResponseSerializer? _rawResponseWriter;
    private readonly StreamingJsonResponseSerializer? _streamingWriter;
    private readonly IContentNegotiationPolicy _negotiationPolicy;
    private readonly Action<IExecutionContext>? _headerActions;
    private readonly TimeSpan _heartbeatInterval;

    /// <param name="serializationLocatorService">
    /// Where a declared content type is resolved to a serializer. Optional, because a host can
    /// assemble a pipeline with no serialization stack in it at all - the bare chains some tests
    /// build, and what <c>ResponseFinalizerFilter</c> guards against for the same reason. Without
    /// one nothing is bound and every response negotiates, which is what those pipelines did before
    /// there was a binding.
    /// </param>
    public IOFilterProvider(
        IContextSerializationService contextSerializationService,
        IOptions<IResponseHeaderConfiguration> responseHeaderConfiguration,
        IOptions<IStreamingConfiguration> streamingConfiguration,
        ISerializationLocatorService? serializationLocatorService = null,
        RawResponseSerializer? rawResponseWriter = null,
        StreamingJsonResponseSerializer? streamingWriter = null,
        IContentNegotiationPolicy? negotiationPolicy = null) {
        _contextSerializationService = contextSerializationService;
        _serializationLocatorService = serializationLocatorService;
        _rawResponseWriter = rawResponseWriter;
        _streamingWriter = streamingWriter;
        _negotiationPolicy = negotiationPolicy ?? new ContentNegotiationPolicy();
        _headerActions = SetupHeaderActions(responseHeaderConfiguration.Value);
        _heartbeatInterval = streamingConfiguration.Value.HeartbeatInterval;
    }

    private Action<IExecutionContext>? SetupHeaderActions(IResponseHeaderConfiguration responseHeaderConfiguration) {
        if (responseHeaderConfiguration.HeaderActions.Count == 0 &&
            responseHeaderConfiguration.CommonHeaders.Count == 0) {
            return null;
        }

        var headerAction = new List<Action<IExecutionContext>>(responseHeaderConfiguration.HeaderActions);

        if (responseHeaderConfiguration.CommonHeaders.Count > 0) {
            var commonList = responseHeaderConfiguration.CommonHeaders;

            headerAction.Add(context => {
                var responseHeaders = context.Response.Headers;

                for (var i = 0; i < commonList.Count; i++) {
                    var kvp = commonList[i];

                    responseHeaders[kvp.Key] = kvp.Value;
                }
            });
        }

        if (headerAction.Count == 1) {
            return headerAction[0];
        }

        return context => {
            for (var i = 0; i < headerAction.Count; i++) {
                headerAction[i].Invoke(context);
            }
        };
    }

    public IExecutionFilter ProvideFilter(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest) {
        return new IoFilter(
            deserializeRequest,
            SerializeResponse(handlerInfo),
            _headerActions
        );
    }

    /// <summary>
    /// What writes this handler's responses, resolved once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work a request used to do. Locating a serializer was a container resolve, an
    /// <c>Accept</c> parse and a search over every registered serializer for every accepted media
    /// type, per response, to arrive at an answer the operation had already stated at build.
    /// </para>
    /// <para>
    /// Composed once per handler, so the closure here is one allocation for the life of the
    /// application rather than one per request.
    /// </para>
    /// </remarks>
    private Func<IExecutionContext, Task> SerializeResponse(IExecutionRequestHandlerInfo handlerInfo) {
        var bound = Bind(handlerInfo, out var declaredContentType);

        if (bound == null) {
            return _contextSerializationService.SerializeResponse;
        }

        return context =>
            _contextSerializationService.SerializeResponse(context, bound, declaredContentType);
    }

    /// <summary>
    /// What writes one item of a stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bound rather than located, and the difference is observable. The framing commits
    /// <c>application/x-ndjson</c> or <c>text/event-stream</c> before the first item, and a
    /// committed content type used to send every item through the locator - where
    /// <c>RawResponseSerializer</c> also claims any committed type for a <c>string</c>, so a stream
    /// of strings came down to which of the two was registered last. It emitted <c>alpha</c>, not
    /// <c>"alpha"</c>, in a format whose entire contract is one JSON document per line.
    /// </para>
    /// <para>
    /// <b>Streaming is a decision the return type already made.</b> There is no <c>Accept</c> a
    /// client can send that turns a buffered handler into a streaming one, so nothing about the item
    /// writer is a per-request question - and locating one per item, for every item, was work to
    /// re-derive an answer fixed when the handler was composed.
    /// </para>
    /// </remarks>
    private Func<IExecutionContext, Task> SerializeStreamedItem() {
        if (_streamingWriter == null) {
            return _contextSerializationService.SerializeResponse;
        }

        // The declared type is passed as null: the framing owns the content type here, and it
        // commits one the writer is expected to write under rather than one it has to match.
        return context => _contextSerializationService.SerializeResponse(context, _streamingWriter, null);
    }

    /// <summary>
    /// The serializer for this handler, or null where only a request can decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A handler returning <c>byte[]</c> or <c>Stream</c> takes the pass-through writer.</b> The
    /// return type is the handler saying it controls its own serialization, so nothing is looked up
    /// and no serializer is consulted on any request.
    /// </para>
    /// <para>
    /// <b>An operation that declares nothing takes the service default.</b> That is the common case
    /// and the one the allocation was measured on: a JSON endpoint a real client calls, whose
    /// <c>Accept</c> header was parsed into a list on every request to reach the same serializer
    /// every time.
    /// </para>
    /// <para>
    /// <b>One declared media type binds only where a mismatch is not a refusal.</b> Under
    /// <see cref="ContentNegotiationMode.Strict"/> a client asking for something the operation does
    /// not produce is answered 406, and that answer needs the header, so those operations keep
    /// negotiating. Under <see cref="ContentNegotiationMode.Lenient"/> the operation returns what it
    /// declares, which is what most JSON APIs do and what HTTP permits.
    /// </para>
    /// <para>
    /// A set of two or more is a genuine choice, and is the one shape that has to read the header.
    /// </para>
    /// </remarks>
    private IResponseSerializer? Bind(
        IExecutionRequestHandlerInfo handlerInfo, out string? declaredContentType) {
        var declared = handlerInfo.ProducedContentTypes;

        declaredContentType = declared.Count == 1 ? declared[0] : null;

        if (handlerInfo.WritesRawBytes && _rawResponseWriter != null) {
            return _rawResponseWriter;
        }

        if (_serializationLocatorService == null) {
            declaredContentType = null;

            return null;
        }

        if (declared.Count == 0) {
            var fallback = _serializationLocatorService.DefaultSerializer;

            declaredContentType = fallback?.ContentType;

            return fallback;
        }

        if (declared.Count > 1 || _negotiationPolicy.Mode != ContentNegotiationMode.Lenient) {
            declaredContentType = null;

            return null;
        }

        var serializer = _serializationLocatorService.ProducerOf(declared[0]);

        if (serializer == null) {
            declaredContentType = null;
        }

        return serializer;
    }

    public IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest) {
        return ProvideAsyncEnumerableFilter<TItem>(handlerInfo, deserializeRequest, null);
    }

    /// <summary>
    /// The streamed filter, framed the way the handler asked for, with the configured heartbeat.
    /// </summary>
    /// <remarks>
    /// An overload rather than a changed signature: <c>IIOFilterProvider</c> is public, and a
    /// generator emitting the three-argument call is the shape every already-generated application
    /// carries. The generator emits this one when a handler names a framing.
    /// </remarks>
    public IExecutionFilter ProvideAsyncEnumerableFilter<TItem>(
        IExecutionRequestHandlerInfo handlerInfo,
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        IStreamFraming? framing) {
        return new AsyncEnumerableIoFilter<TItem>(
            deserializeRequest,
            SerializeStreamedItem(),
            _headerActions,
            framing,
            _heartbeatInterval
        );
    }
}
