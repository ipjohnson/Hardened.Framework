using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// How this service answers a client asking for a media type no operation of it produces.
/// </summary>
/// <remarks>
/// <para>
/// On the entry point, and one answer for the whole service - the same shape and the same place as
/// <c>[CaseInsensitiveRoutes]</c>. Deliberately not per operation: a policy that has to be repeated
/// is one that ends up applied unevenly, and a single operation quietly negotiating while every
/// other refuses is worse than either answer applied consistently. The omission would be invisible.
/// </para>
/// <para>
/// The set of media types an operation produces <em>is</em> per operation, because operations
/// genuinely differ - see <c>[SupportedContentTypes]</c> and the <c>content:</c> keys of a
/// described response. This decides only what happens outside that set.
/// </para>
/// <para>
/// A description says the same thing with <c>x-hardened-content-negotiation</c> at its root.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public class ContentNegotiationAttribute : Attribute {
    public ContentNegotiationAttribute(ContentNegotiationMode mode) {
        Mode = mode;
    }

    public ContentNegotiationMode Mode { get; }
}
