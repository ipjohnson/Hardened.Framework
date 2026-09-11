namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// The media types a serializer in this assembly writes a model as.
/// </summary>
/// <remarks>
/// <para>
/// <b>For the build, not for the runtime.</b> Nothing reads this at request time: a serializer is
/// located by its own <see cref="Serializer.IResponseSerializer.ContentType"/>, and registering one
/// is what makes a media type producible. This is how a compilation that only references the
/// package can know the same thing.
/// </para>
/// <para>
/// It exists because <c>HRDR012</c> asks a question the build otherwise cannot answer. A property's
/// value is not readable from metadata, so a generator looking at a referenced
/// <c>IResponseSerializer</c> can see the type and not the media type it declares - which made the
/// warning fire on every operation that declared anything but JSON, including the ones that had the
/// serializer they needed. The alternative was to drop the check, and the check is worth keeping:
/// <c>[Produces("text/csv")]</c> with no CSV serializer anywhere is a real mistake that otherwise
/// surfaces as a 500 in an environment.
/// </para>
/// <para>
/// Write it once per assembly that ships a serializer, naming what that serializer declares. An
/// application with its own <c>IResponseSerializer</c> writes it too, in any file:
/// <c>[assembly: WritesContentType("application/x-msgpack")]</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [assembly: WritesContentType("application/x-msgpack")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class WritesContentTypeAttribute : Attribute {
    public WritesContentTypeAttribute(params string[] contentTypes) {
        ContentTypes = contentTypes;
    }

    /// <summary>The media types a serializer here writes.</summary>
    public string[] ContentTypes { get; }
}
