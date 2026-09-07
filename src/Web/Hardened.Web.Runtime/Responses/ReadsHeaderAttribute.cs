namespace Hardened.Web.Runtime.Responses;

/// <summary>
/// States that an operation reads a request header its handler does not bind, so the published
/// document declares the parameter.
///
/// <code>
/// [ReadsHeader("If-None-Match", Description = "A tag a previous response carried.")]
/// public sealed class ConditionalGetAttribute : Attribute, IRequestFilterProvider { }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A filter that reads a header reads it before the handler runs, so nothing in the handler's
/// signature mentions it and the document published no parameter for it. A generated client
/// therefore had no way to send it: <c>[ConditionalGet]</c> answers a conditional request and no
/// client generated from the document could make one.
/// </para>
/// <para>
/// The counterpart of <see cref="AnswersHeaderAttribute"/>, read the same two ways - on a handler
/// or its class, and on a declaration's own type - and dropped where the operation already binds a
/// parameter of that name, because the handler's own is the more specific description.
/// </para>
/// <para>
/// Always optional. A header a filter reads is one the caller may send; a header the operation
/// requires is one the handler binds, and binding it is how the 400 for its absence gets declared
/// too.
/// </para>
/// </remarks>
/// <param name="name">The header's name, as it goes on the wire.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = true, Inherited = true)]
public sealed class ReadsHeaderAttribute(string name) : Attribute {

    /// <summary>The header's name, as it goes on the wire.</summary>
    public string Name { get; } = name;

    /// <summary>The parameter's <c>description</c>, or null to publish it unexplained.</summary>
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
