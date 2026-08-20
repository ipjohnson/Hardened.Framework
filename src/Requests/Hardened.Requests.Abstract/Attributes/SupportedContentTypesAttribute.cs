namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The media types this operation can produce, in preference order.
/// </summary>
/// <remarks>
/// <para>
/// The hand-written half of what a description states with the <c>content:</c> keys of its success
/// response. Both reach <c>ResponseInformationModel.ProducedContentTypes</c>, so an application
/// written in code and one generated from a document negotiate the same way.
/// </para>
/// <para>
/// Not <see cref="RawResponseAttribute"/>, which is a different thing: that assigns the response's
/// content type before the handler runs and takes it out of negotiation entirely - "this <em>is</em>
/// a PDF". This says what the operation is able to produce and lets the client choose among them.
/// </para>
/// <para>
/// Order is the server's preference, and it decides what <c>Accept: */*</c> - or a request with no
/// <c>Accept</c> at all - is answered with. A client that names types explicitly gets its own
/// preference order honoured instead.
/// </para>
/// <para>
/// What happens when a client asks for something outside this set is not decided here. It is one
/// answer for a whole service rather than per operation, because a policy that has to be repeated
/// is a policy that ends up applied unevenly - see <c>ContentNegotiationAttribute</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/reports/shelf")]
/// [SupportedContentTypes("text/plain", "text/csv")]
/// public string Shelf() => ...;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class SupportedContentTypesAttribute : Attribute {
    public SupportedContentTypesAttribute(params string[] contentTypes) {
        ContentTypes = contentTypes;
    }

    /// <summary>The media types, in the order the server prefers them.</summary>
    public string[] ContentTypes { get; }
}
