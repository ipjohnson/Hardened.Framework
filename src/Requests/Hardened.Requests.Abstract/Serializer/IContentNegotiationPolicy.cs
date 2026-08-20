namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// One answer, for the whole service, to a client asking for a media type no operation produces.
/// </summary>
/// <remarks>
/// Registered by the generated routing table from <c>[ContentNegotiation]</c> on the entry point or
/// <c>x-hardened-content-negotiation</c> at a description's root, and defaulted to
/// <see cref="ContentNegotiationMode.Strict"/> where neither says otherwise.
/// </remarks>
public interface IContentNegotiationPolicy {
    ContentNegotiationMode Mode { get; }
}

/// <inheritdoc />
public sealed class ContentNegotiationPolicy : IContentNegotiationPolicy {
    public ContentNegotiationPolicy(ContentNegotiationMode mode = ContentNegotiationMode.Strict) {
        Mode = mode;
    }

    public ContentNegotiationMode Mode { get; }
}
