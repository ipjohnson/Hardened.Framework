namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The media types this operation produces, in preference order.
/// </summary>
/// <remarks>
/// <para>
/// <b>One value is a declaration and takes the fast path.</b> The generator stamps it into the
/// operation model, the serializer for it is resolved once as the handler's pipeline is composed,
/// and a request costs no <c>Accept</c> parse, no search and no container resolve. Nothing declared
/// means the service default, which is JSON.
/// </para>
/// <para>
/// <b>More than one value is a set, and negotiates.</b> The client's own preference order decides
/// within it, and the first value is what a client expressing no preference is answered with -
/// <c>Accept: */*</c>, or no <c>Accept</c> at all - because the first representation a document
/// lists is the one it leads with. What happens when a client asks for something outside the set is
/// not decided here: it is one answer for a whole service, because a policy that has to be repeated
/// is one that ends up applied unevenly. See <c>ContentNegotiationAttribute</c>.
/// </para>
/// <para>
/// This is the hand-written half of what a description states with the <c>content:</c> keys of its
/// success response. Both reach <c>ResponseInformationModel.ProducedContentTypes</c>, so an
/// application written in code and one generated from a document behave the same way.
/// </para>
/// <para>
/// <b>It is a statement about the response only.</b> An operation that accepts JSON and returns CSV
/// is ordinary, and a description separates <c>requestBody.content</c> from
/// <c>responses.*.content</c> for that reason. The inbound <c>Content-Type</c> is read and
/// dispatched on by a different mechanism entirely.
/// </para>
/// <para>
/// It replaced <c>[SupportedContentTypes]</c> and <c>[RawResponse]</c>, which stated two facts
/// between them - the media type, and what the return value already is. The second is in the
/// signature. A handler returning <c>byte[]</c> or <c>Stream</c> writes its bytes out unchanged
/// whatever this names, because those shapes are the handler saying it controls its own
/// serialization; a handler returning a model is serialized as whatever this names.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/reports/{id}")]
/// [Produces("application/pdf")]
/// public byte[] Report(int id) => ...;
///
/// [Get("/reports/shelf")]
/// [Produces("text/plain", "text/csv")]
/// public string Shelf() => ...;
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly,
    AllowMultiple = false)]
public class ProducesAttribute : Attribute {
    public ProducesAttribute(params string[] contentTypes) {
        ContentTypes = contentTypes;
    }

    /// <summary>The media types, in the order the server prefers them.</summary>
    public string[] ContentTypes { get; }
}
