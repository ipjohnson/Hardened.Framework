namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// States that a response carries a header, so the published document says so.
///
/// <code>
/// [Post("/jobs")]
/// [AnswersHeader(201, "Location", Description = "Where the job was created.")]
/// public Job Create(NewJob job) { ... }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>The declaration, not the value.</b> A document says <em>201 carries a Location</em>; it
/// cannot say what that Location is, because the value comes from the resource the handler just
/// made. The same division <c>Created&lt;T&gt;</c> makes, where the type carries the header and the
/// caller carries its value - and this is how a handler that is not returning a
/// <c>Created&lt;T&gt;</c> says the same thing. In throws mode there was no way to say it at all:
/// the handler set the header on the context and the document described the response as bare.
/// </para>
/// <para>
/// <b>On a handler, or on a declaration's type.</b> Written on a method or its class it describes
/// that operation. Written on an attribute's own type - the way <c>[AnswersStatus]</c> is - it
/// describes every operation carrying that attribute, which is how <c>[ConditionalGet]</c>
/// publishes the <c>ETag</c> it writes without the document generator knowing what the filter does.
/// </para>
/// <para>
/// The status need not be one the handler declares. A header on a status nothing else publishes is
/// dropped rather than inventing the response, because a header is a fact about a response and not
/// a response.
/// </para>
/// <para>
/// No schema type, for the reason a response header never has one here: a header is a string on the
/// wire whatever it carries, and a quoted ETag is the header that settled it.
/// </para>
/// </remarks>
/// <param name="status">The status whose response carries the header.</param>
/// <param name="name">The header's name, as it goes on the wire.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = true, Inherited = true)]
public sealed class AnswersHeaderAttribute(int status, string name) : Attribute {

    /// <summary>The status whose response carries the header.</summary>
    public int Status { get; } = status;

    /// <summary>The header's name, as it goes on the wire.</summary>
    public string Name { get; } = name;

    /// <summary>The header's <c>description</c>, or null to publish it unexplained.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The HTTP methods this reaches, comma-separated, or null for every method.
    /// </summary>
    /// <remarks>
    /// For a declaration written on a class, where it covers operations a filter stands down on.
    /// <c>[ConditionalGet]</c> on a controller installs on the reads and on nothing else, so
    /// without this the document claimed a 304 on the writes beside them.
    /// </remarks>
    public string? Methods { get; set; }

    /// <summary>
    /// Whether a handler that streams its response is left out, for the reason
    /// <see cref="Methods"/> exists.
    /// </summary>
    public bool NotWhenStreaming { get; set; }
}
