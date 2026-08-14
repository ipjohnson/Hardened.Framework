namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// Commits a handler's response to a content type, and writes its value without structuring it.
/// </summary>
/// <remarks>
/// <para>
/// The generator reads this as response information rather than as a filter and assigns the content
/// type onto <c>IExecutionResponse.ContentType</c> before the handler runs.
/// <c>RawResponseSerializer</c> then claims the response through ordinary serializer selection, and
/// writes a string, <c>byte[]</c> or <c>Stream</c> straight to the body.
/// </para>
/// <para>
/// Because the content type is committed rather than negotiated, the client cannot overrule it - a
/// handler that says it returns a PDF returns a PDF whatever <c>Accept</c> asked for. That is the
/// difference between this and simply returning a string, which is offered as <c>text/plain</c> but
/// serialised as JSON for a client that asks for JSON.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class RawResponseAttribute : Attribute {
    /// <remarks>
    /// The argument used to be accepted and dropped on the floor - no property, no field. The
    /// generator reads the value off the syntax node rather than off an instance, so the emitted
    /// code was right and nothing failed; the attribute was simply unreadable to anything else,
    /// including reflection over a handler's metadata.
    /// </remarks>
    public RawResponseAttribute(string contentType = "text/plain") {
        ContentType = contentType;
    }

    public string ContentType { get; }
}
