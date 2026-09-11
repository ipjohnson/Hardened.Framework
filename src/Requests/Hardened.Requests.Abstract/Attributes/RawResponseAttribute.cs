namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// Commits a handler's response to a content type, and writes its value without structuring it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Superseded by <see cref="ProducesAttribute"/>, which it now derives from.</b> The two facts
/// this stated - the media type, and that the return value is already bytes - are the media type
/// and the return type, and the return type is in the signature. A handler returning
/// <c>byte[]</c> or <c>Stream</c> takes the pass-through writer whatever it declares, and a handler
/// returning a <c>string</c> takes it by declaring a media type nothing else writes.
/// </para>
/// <para>
/// Kept for one release so that the break is announced rather than discovered.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
[Obsolete("Use [Produces] instead. [RawResponse(\"text/csv\")] is [Produces(\"text/csv\")].")]
public class RawResponseAttribute : ProducesAttribute {
    public RawResponseAttribute(string contentType = "text/plain") : base(contentType) {
        ContentType = contentType;
    }

    public string ContentType { get; }
}
