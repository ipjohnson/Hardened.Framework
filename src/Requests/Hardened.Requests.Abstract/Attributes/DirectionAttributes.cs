namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The server owns this value: it appears in responses, and a client must not send it.
/// </summary>
/// <remarks>
/// <para>
/// OpenAPI's <c>readOnly</c>. It says which direction a value travels, which is a fact about the
/// contract rather than about the type - the same C# property carries the value in both directions,
/// and the description is only constraining one of them.
/// </para>
/// <para>
/// Documentation today. The generated model reads and writes the property normally, which is what
/// lets a response containing it round-trip: withholding the setter instead made the value
/// unreadable, so a client deserializing a response silently lost every <c>created_at</c> and
/// <c>id</c> the server sent - the shape being guarded against on the way in, dropped on the way
/// out.
/// </para>
/// <para>
/// Enforcement belongs to validation, where a request that sets one can be rejected with the reason
/// and the property name, rather than to serialization, where it can only be dropped in silence.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ResponseOnlyAttribute : Attribute {
}

/// <summary>
/// The client owns this value: it is accepted in requests, and never returned.
/// </summary>
/// <remarks>
/// OpenAPI's <c>writeOnly</c>, and the mirror of <see cref="ResponseOnlyAttribute"/> - a password
/// on a create request being the usual one. Withholding the getter made such a property
/// unserializable, so a model could not write the request body it exists to describe.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class RequestOnlyAttribute : Attribute {
}
