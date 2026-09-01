using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class SerializationLocatorService : ISerializationLocatorService {
    private readonly IRequestDeserializer[] _requestDeserializers;
    private readonly IResponseSerializer[] _responseSerializers;
    private readonly IContentNegotiationPolicy _negotiationPolicy;

    public SerializationLocatorService(
        IEnumerable<IRequestDeserializer> requestDeserializers,
        IEnumerable<IResponseSerializer> responseSerializers,
        IContentNegotiationPolicy? negotiationPolicy = null) {
        _negotiationPolicy = negotiationPolicy ?? new ContentNegotiationPolicy();

        // Reversed so an application's own registrations are tested before the framework's, then
        // ordered ahead of that for the reason given below. Same treatment as the response side,
        // and for the same reason: two deserializers both claiming application/json - which is
        // what installing Hardened.Requests.Serializers.Newtonsoft produces - were previously
        // separated only by which module happened to register last.
        _requestDeserializers = requestDeserializers
            .Reverse()
            .OrderBy(deserializer => deserializer.Order)
            .ToArray();

        // Ordered ahead of that, because reverse-registration order alone is not something an
        // application can steer: within a module DependencyModules sorts by implementation type
        // name, so which serializer won a contested response came down to how two class names
        // sorted. OrderBy is a stable sort, so serializers sharing an order keep the
        // reverse-registration relationship and an application's own still beats the framework's.
        //
        // Sorted here rather than per request - this service is a singleton, so it happens once.
        _responseSerializers = responseSerializers
            .Reverse()
            .OrderBy(serializer => serializer.Order)
            .ToArray();
    }

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

        // Parsed once per response rather than once per serializer.
        var accepted = AcceptedContentTypes.Parse(context.Request.Accept);
        var mediaTypes = accepted.MediaTypes;

        // What the operation says it produces, when it says anything.
        //
        // Without this the client's preferences were matched against every registered serializer
        // rather than against the operation's own representations - and MediaType.Matches answers
        // true for */* and for an absent Accept against any of them. So an operation declaring
        // text/plain and nothing else was answered in JSON for `Accept: */*`, which is what curl
        // sends by default: the declared string, wrapped in quotes with its newlines escaped.
        var declared = context.HandlerInfo?.ProducedContentTypes;

        if (declared is { Count: > 0 }) {
            return FindDeclaredProducer(declared, mediaTypes, context);
        }

        // Nothing declared, so every registered serializer is a candidate - which is what this did
        // for every response before an operation could say what it produces, and still does for a
        // handler that says nothing.
        for (var i = 0; i < mediaTypes.Count; i++) {
            var serializer = FindProducerOf(mediaTypes[i], context);

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
        IReadOnlyList<string> accepted,
        IExecutionContext context) {
        // The client's preferences decide the order, the declared set decides what is on offer.
        for (var i = 0; i < accepted.Count; i++) {
            for (var j = 0; j < declared.Count; j++) {
                if (!MediaType.Matches(accepted[i], declared[j])) {
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
}