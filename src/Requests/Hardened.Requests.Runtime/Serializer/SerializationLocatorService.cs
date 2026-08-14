using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Serializer;

[SingletonService(Using = RegistrationType.Try)]
public class SerializationLocatorService : ISerializationLocatorService {
    private readonly IRequestDeserializer[] _requestDeserializers;
    private readonly IResponseSerializer[] _responseSerializers;

    public SerializationLocatorService(
        IEnumerable<IRequestDeserializer> requestDeserializers,
        IEnumerable<IResponseSerializer> responseSerializers) {
        // reverse lists so user registered serializers are tested first
        _requestDeserializers = requestDeserializers.Reverse().ToArray();

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
            throw new Exception(
                $"Response committed to content type '{committedContentType}' but no registered " +
                "serializer can produce it.");
        }

        // Parsed once per response rather than once per serializer.
        var accepted = AcceptedContentTypes.Parse(context.Request.Accept);
        var mediaTypes = accepted.MediaTypes;

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

    private IResponseSerializer? FindProducerOf(string mediaType, IExecutionContext context) {
        for (var i = 0; i < _responseSerializers.Length; i++) {
            if (_responseSerializers[i].CanProduce(mediaType, context)) {
                return _responseSerializers[i];
            }
        }

        return null;
    }
}