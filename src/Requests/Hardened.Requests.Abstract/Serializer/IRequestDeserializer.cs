using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// Where a request deserializer sits relative to the others. Lower is asked first.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="ResponseSerializerOrder"/>, and it exists for the same reason: the
/// alternative is registration order, and registration order is not something an application can
/// steer. Within a module DependencyModules sorts by whether a registration is conditional and then
/// by implementation type name, so which deserializer read a body came down to how two class names
/// happened to sort.
/// </para>
/// <para>
/// Separate from <see cref="ResponseSerializerOrder"/> rather than shared, because the two do not
/// have the same tiers — there is no request-side equivalent of
/// <see cref="ResponseSerializerOrder.Template"/>. Values line up with the response enum where the
/// tiers do correspond, so a serializer pair can state one precedence for both directions.
/// </para>
/// <para>
/// Values are spaced so a deserializer can be slotted between two of them without renumbering.
/// </para>
/// </remarks>
public enum RequestDeserializerOrder {
    /// <summary>
    /// A deserializer for one specific content type, or one an application installed to replace the
    /// default. Ahead of the general-purpose ones.
    /// </summary>
    Specialized = -100,

    /// <summary>The default, and where the built-in JSON deserializer sits.</summary>
    Normal = 0,

    /// <summary>
    /// Behind the general-purpose deserializers. One here reads a body only when nothing else
    /// claimed the content type.
    /// </summary>
    Deferred = 1000
}

public interface IRequestDeserializer {
    bool IsDefaultSerializer { get; }

    /// <summary>
    /// Lower is asked first. Defaults to <see cref="RequestDeserializerOrder.Normal"/>, so an
    /// existing deserializer that does not care keeps working unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="IsDefaultSerializer"/> on purpose, exactly as on
    /// <see cref="IResponseSerializer"/>: order decides who is asked first;
    /// <c>IsDefaultSerializer</c> decides who reads a body nobody claimed.
    /// </para>
    /// <para>
    /// Added 2026-08-18. Before it, two deserializers both claiming <c>application/json</c> — which
    /// is what installing <c>Hardened.Requests.Serializers.Newtonsoft</c> produces — were separated
    /// only by reverse registration order, so the package that exists to replace the default JSON
    /// reader could not reliably do it.
    /// </para>
    /// </remarks>
    int Order => (int)RequestDeserializerOrder.Normal;

    bool CanProcessContext(IExecutionContext context);

    ValueTask<T?> DeserializeRequestBody<T>(IExecutionContext context);
}
