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
    /// The same tag <see cref="SystemTextJsonResponseSerializer"/> declares, and this one wins by
    /// registering after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How an AOT application ends up using its own serializer rather than the reflection-based one.
    /// <c>AotSerializerModule</c> is imported by the application, so its registration lands after
    /// <c>HardenedRequestModule</c>'s and is the one the registry keeps for
    /// <c>application/json</c>. <c>SerializerRegistrationOrderTests</c> pins the rule, because it is
    /// the whole of the precedence now.
    /// </para>
    /// <para>
    /// This was an <c>Order</c> before, and registration order with <c>TryAddSingleton</c> before
    /// that. The Try switch keyed on the service type rather than on a class, so adding any third
    /// serializer silently unregistered JSON everywhere.
    /// </para>
    /// </remarks>
    public string ContentType => KnownContentType.Json;

    public async Task SerializeResponse(IExecutionContext context) {
        context.Response.ContentType = "application/json";

        if (context.Response.ResponseValue == null) {
            return;
        }

        await System.Text.Json.JsonSerializer.SerializeAsync(
            context.Response.Body,
            context.Response.ResponseValue,
            Hardened.Shared.Runtime.Json.JsonTypeInfoLookup.For(_serializerOptions, context.Response.ResponseValue));
    }
}
