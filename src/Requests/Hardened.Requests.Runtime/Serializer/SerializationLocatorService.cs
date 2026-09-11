using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class SerializationLocatorService : ISerializationLocatorService {
    private readonly IRequestDeserializer[] _requestDeserializers;
    private readonly IResponseSerializer[] _responseSerializers;
    private readonly Dictionary<string, IResponseSerializer> _byContentType;
    private readonly IResponseSerializer? _defaultSerializer;
    private readonly IContentNegotiationPolicy _negotiationPolicy;

    public SerializationLocatorService(
        IEnumerable<IRequestDeserializer> requestDeserializers,
        IEnumerable<IResponseSerializer> responseSerializers,
        IContentNegotiationPolicy? negotiationPolicy = null) {
        _negotiationPolicy = negotiationPolicy ?? new ContentNegotiationPolicy();

        // Reversed so an application's own registrations are tested before the framework's, then
        // ordered ahead of that: within a module DependencyModules sorts by implementation type
        // name, so which deserializer read a body came down to how two class names sorted. Two
        // deserializers both claiming application/json is what installing
        // Hardened.Requests.Serializers.Newtonsoft produces.
        //
        // The response side dropped its Order and this did not. A deserializer is chosen by a
        // predicate over the whole request rather than by a tag, so there is nothing to key a
        // registry on.
        _requestDeserializers = requestDeserializers
            .Reverse()
            .OrderBy(deserializer => deserializer.Order)
            .ToArray();

        // Reversed, so the last registration under a content type is the first one asked. That is
        // the whole of response-side precedence now: a serializer declares the media type it writes
        // and an application's own registration lands after the framework's, so importing a package
        // that replaces JSON is enough to be sure it is used.
        //
        // There used to be an Order here as well, because reverse-registration order within a module
        // is decided by how implementation type names sort. Selection no longer runs through this
        // array for an operation that declares what it produces - the serializer is resolved once as
        // the handler's pipeline is composed - so there is nothing left for an order to adjudicate.
        //
        // Reversed here rather than per request - this service is a singleton, so it happens once.
        _responseSerializers = responseSerializers
            .Reverse()
            .ToArray();

        // One entry per content type, first writer wins - and the array is already reversed, so the
        // first is the last registration. Built here rather than per lookup: this service is a
        // singleton, and the lookups happen as handlers are composed.
        _byContentType = new Dictionary<string, IResponseSerializer>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _responseSerializers.Length; i++) {
            var serializer = _responseSerializers[i];

            if (!string.IsNullOrEmpty(serializer.ContentType) &&
                !_byContentType.ContainsKey(serializer.ContentType)) {
                _byContentType[serializer.ContentType] = serializer;
            }

            if (_defaultSerializer == null && serializer.IsDefaultSerializer) {
                _defaultSerializer = serializer;
            }
        }
    }

    /// <inheritdoc />
    public IResponseSerializer? ProducerOf(string contentType) =>
        _byContentType.TryGetValue(contentType, out var serializer) ? serializer : null;

    /// <inheritdoc />
    public IResponseSerializer? DefaultSerializer => _defaultSerializer;

    public IRequestDeserializer FindRequestDeserializer(IExecutionContext context) {
        IRequestDeserializer? defaultSerializer = null;

        for (var i = 0; i < _requestDeserializers.Length; i++) {
            var requestDeserializer = _requestDeserializers[i];

            if (requestDeserializer.CanProcessContext(context)) {
                return requestDeserializer;
            }

            if (requestDeserializer.IsDefaultSerializer) {
                defaultSerializer ??= requestDeserializer;
            }
        }

        if (defaultSerializer != null) {
            return defaultSerializer;
        }

        throw new Exception("Could not find serializer: " + context.Request.ContentType);
    }

    /// <summary>
    /// Which serializer writes this response, in three tiers: a content type the response has
    /// already committed to, then what the client asked for, then the default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The negotiation loop runs the client's preferences on the outside and the serializers on the
    /// inside, which is what makes the client's ranking decide rather than the framework's. A
    /// request for <c>application/json,text/html;q=0.9</c> against a route that renders a view is
    /// served as JSON, because JSON is asked about first - not because JSON happens to be ordered
    /// ahead of the template serializer, which it is not.
    /// </para>
    /// <para>
    /// <c>Order</c> is the inner loop, so it decides only among serializers that satisfy the same
    /// preference. That includes <c>*/*</c>, where everything qualifies and the server's own
    /// ranking is the only thing left - which is exactly the case it should decide.
    /// </para>
    /// </remarks>
    public IResponseSerializer FindResponseSerializer(IExecutionContext context) {
        // A response that already carries a content type has committed to it - [RawResponse], or a
        // handler that set it outright. The client does not get to overrule that; the point of
        // saying "this is a PDF" is that it is a PDF.
        var committedContentType = context.Response.ContentType;

        if (!string.IsNullOrEmpty(committedContentType)) {
            var committed = FindProducerOf(committedContentType!, context);

            if (committed != null) {
                return committed;
            }

            // Falling through to JSON here would answer a request for application/pdf with a JSON
            // document and no indication anything went wrong. Nothing registered can write what this
            // response promised, which is a configuration problem rather than a client one.
            throw new ContentTypeNotProducibleException(
                $"Response committed to content type '{committedContentType}' but no registered " +
                "serializer can produce it.");
        }

        var accept = context.Request.Accept;

        // What the operation says it produces, when it says anything.
        //
        // Without this the client's preferences were matched against every registered serializer
        // rather than against the operation's own representations - and MediaType.Matches answers
        // true for */* and for an absent Accept against any of them. So an operation declaring
        // text/plain and nothing else was answered in JSON for `Accept: */*`, which is what curl
        // sends by default: the declared string, wrapped in quotes with its newlines escaped.
        var declared = context.HandlerInfo?.ProducedContentTypes;

        if (declared is { Count: > 0 }) {
            return FindDeclaredProducer(declared, accept, context);
        }

        // Nothing declared, so every registered serializer is a candidate - which is what this did
        // for every response before an operation could say what it produces, and still does for a
        // handler that says nothing.
        foreach (var requested in MediaType.Enumerate(accept)) {
            var serializer = FindProducerOf(requested, context);

            if (serializer != null) {
                return serializer;
            }
        }

        for (var i = 0; i < _responseSerializers.Length; i++) {
            if (_responseSerializers[i].IsDefaultSerializer) {
                return _responseSerializers[i];
            }
        }

        throw new Exception("Could not locate response serializer for accept: " + context.Request.Accept);
    }


    /// <summary>
    /// The serializer for a response whose operation declared what it produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two of the three cases need no policy at all. <c>*/*</c>, or no <c>Accept</c>, means "whatever
    /// you have" and is answered with the first declared type - the one the document leads with. A
    /// client naming types gets its own preference order honoured, within the declared set.
    /// </para>
    /// <para>
    /// Only the third is a decision: a client naming types that share nothing with the set has asked
    /// for something that does not exist. That is a 406 under
    /// <see cref="ContentNegotiationMode.Strict"/>, and falls back to the default serializer under
    /// <see cref="ContentNegotiationMode.Lenient"/>.
    /// </para>
    /// </remarks>
    private IResponseSerializer FindDeclaredProducer(
        IReadOnlyList<string> declared,
        string? accept,
        IExecutionContext context) {
        // The client's preferences decide the order, the declared set decides what is on offer.
        foreach (var requested in MediaType.Enumerate(accept)) {
            for (var j = 0; j < declared.Count; j++) {
                if (!MediaType.Matches(requested, declared[j])) {
                    continue;
                }

                var serializer = FindProducerOf(declared[j], context);

                if (serializer != null) {
                    context.Response.ContentType = declared[j];

                    return serializer;
                }
            }
        }

        // Nothing the client asked for is on offer - but first tell that apart from a service that
        // cannot write anything it promised. A document declaring application/pdf with no PDF
        // serializer registered would otherwise answer 406 to every request and make a
        // configuration fault look like a client mistake. Tier one throws for exactly this, and so
        // does this.
        var producible = false;

        for (var j = 0; j < declared.Count && !producible; j++) {
            producible = FindProducerOf(declared[j], context) != null;
        }

        if (!producible) {
            throw new ContentTypeNotProducibleException(
                $"This operation declares {string.Join(", ", declared)} and no registered " +
                "serializer can produce any of them.");
        }

        if (_negotiationPolicy.Mode == ContentNegotiationMode.Lenient) {
            for (var i = 0; i < _responseSerializers.Length; i++) {
                if (_responseSerializers[i].IsDefaultSerializer) {
                    return _responseSerializers[i];
                }
            }
        }

        throw new NotAcceptableException(declared);
    }

    private IResponseSerializer? FindProducerOf(string mediaType, IExecutionContext context) {
        for (var i = 0; i < _responseSerializers.Length; i++) {
            if (_responseSerializers[i].CanProduce(mediaType, context)) {
                return _responseSerializers[i];
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="FindProducerOf(string,IExecutionContext)"/> for one entry of an <c>Accept</c>
    /// header, which is a span rather than a string.
    /// </summary>
    /// <remarks>
    /// <see cref="IResponseSerializer.CanProduce"/> takes a string, and the two serializers that
    /// override it read the response rather than the media type, so the tag is compared here and
    /// they are asked about their own. That is what lets the header stay a span all the way down:
    /// the interface no longer reaches back up the call stack and demands a substring per candidate.
    /// </remarks>
    private IResponseSerializer? FindProducerOf(ReadOnlySpan<char> mediaType, IExecutionContext context) {
        for (var i = 0; i < _responseSerializers.Length; i++) {
            var serializer = _responseSerializers[i];

            if (MediaType.Matches(mediaType, serializer.ContentType) &&
                serializer.CanProduce(serializer.ContentType, context)) {
                return serializer;
            }
        }

        return null;
    }
}