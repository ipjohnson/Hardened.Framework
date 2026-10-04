using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hardened.Requests.Runtime.Serializer;

public class AotRequestDeserializer : IRequestDeserializer
{
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger<AotRequestDeserializer> _logger;

    public AotRequestDeserializer(
        IOptions<IJsonSerializerConfiguration> configuration,
        ILogger<AotRequestDeserializer> logger,
        IEnumerable<IJsonTypeInfoResolver> resolvers
    )
    {
        _logger = logger;

        // Build options without a default reflection-based resolver so that
        // tests fail the same way AOT production does when source-gen type
        // registrations are missing.
        var sourceOptions = configuration.Value.DeSerializerOptions;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = sourceOptions?.PropertyNameCaseInsensitive ?? true,
            PropertyNamingPolicy =
                sourceOptions?.PropertyNamingPolicy ?? JsonNamingPolicy.CamelCase,
            NumberHandling =
                sourceOptions?.NumberHandling ?? JsonNumberHandling.AllowReadingFromString,
        };

        // Copy converters from configured options
        if (sourceOptions != null)
        {
            foreach (var converter in sourceOptions.Converters)
            {
                _serializerOptions.Converters.Add(converter);
            }
        }

        foreach (var resolver in resolvers)
        {
            _serializerOptions.TypeInfoResolverChain.Add(resolver);

            // Pull converters from source-generated contexts (e.g. UnixEpochDateTimeConverter)
            if (resolver is JsonSerializerContext ctx)
            {
                foreach (var converter in ctx.Options.Converters)
                {
                    if (!_serializerOptions.Converters.Contains(converter))
                    {
                        _serializerOptions.Converters.Add(converter);
                    }
                }
            }
        }

        // Reflection last, and only where it is available - see WithReflectionFallback.
        //
        // This call used to sit inside the `resolver is JsonSerializerContext` branch above, nested
        // in the loop. That put it two mistakes deep: it ran only when a resolver happened to be a
        // JsonSerializerContext, and by then the chain was non-empty, so its `TypeInfoResolver is
        // null` guard was already false. It could never install anything. Out here it runs once,
        // after the chain is built, which is what the guard is written for - reflection only when
        // nothing else was registered at all.
        Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.WithReflectionFallback(_serializerOptions);

        // After that call, because it is guarded on the chain being empty and this would fill it.
        _serializerOptions.TypeInfoResolverChain.Add(
            Hardened.Shared.Runtime.Json.PrimitiveJsonTypeInfoResolver.Instance
        );

        // Last, so a DateTimeOffset converter the application configured or a context carries is
        // asked first.
        _serializerOptions.Converters.Add(OffsetRequiredDateTimeOffsetConverter.Instance);
    }

    public bool IsDefaultSerializer => true;

    /// <summary>
    /// Ahead of <see cref="SystemTextJsonRequestDeserializer"/>, which is how an AOT application
    /// ends up reading bodies with its own deserializer rather than the reflection-based one.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="AotResponseSerializer.ContentType"/>, and stated for the same reason.
    /// This used to be arranged by <c>TryAddSingleton</c> on the reflection-based registration, which
    /// keys on the service type rather than on that class - so it fired for any
    /// <c>IRequestDeserializer</c> registered first, not only this one.
    /// </remarks>
    public int Order => (int)RequestDeserializerOrder.Specialized;

    public bool CanProcessContext(IExecutionContext context)
    {
        return MediaType.IsJson(context.Request.ContentType);
    }

    public IReadOnlyList<string> ContentTypes { get; } = [KnownContentType.Json];

    /// <summary>
    /// Reads the body as it is. A compressed body was decoded by <c>RequestDecompressionFilter</c>
    /// before the bind, which is why this no longer looks at <c>Content-Encoding</c>.
    /// </summary>
    public ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context) =>
        JsonRequestBody.Read(
            context,
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.For<T>(_serializerOptions)
        );
}
