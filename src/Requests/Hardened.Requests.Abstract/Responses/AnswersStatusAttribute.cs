namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// States that an operation carrying this declaration can be answered with a status its handler
/// never returns, so the published document says so.
///
/// <code>
/// [AnswersStatus(429, typeof(ErrorModel))]
/// public class RateLimitAttribute : Attribute, IRequestFilterProvider { }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>Written on the declaration, read by the generator.</b> A filter that can refuse a request
/// answers a status the handler's return type says nothing about, so an operation guarded by one
/// published a document that was true about its success and silent about its refusal - and a
/// generated client had no case for the refusal it would actually be sent. This is how the
/// declaration contributes that response without the document generator knowing what any
/// particular filter does.
/// </para>
/// <para>
/// <b>It goes on the attribute's type, or on an interface the type implements.</b> The interface is
/// what makes it reach declarations this framework never sees:
/// <c>IAuthorizeAttribute</c> carries the 401 and the 403, so an application's own authorization
/// attribute publishes them without doing anything, and the same would be true of a filter
/// vocabulary a package invented.
/// </para>
/// <para>
/// It describes what the <em>pipeline</em> answers. A status a handler returns itself belongs on
/// the return type or on <c>[Throws&lt;T&gt;]</c>, which reach the document by their own paths and
/// into the same list.
/// </para>
/// </remarks>
/// <param name="status">The status an operation carrying this declaration may be answered with.</param>
/// <param name="body">
/// The envelope that status carries. Every refusal the framework raises is serialized through
/// <c>ExceptionToModelConverter</c>, so that is <c>ErrorModel</c> for all of them; a declaration
/// answering something else names it here.
/// </param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = true)]
public sealed class AnswersStatusAttribute(int status, Type body) : Attribute {

    /// <summary>The status, unless <see cref="StatusFrom"/> names a property that overrode it.</summary>
    public int Status { get; } = status;

    /// <summary>The envelope the status carries.</summary>
    public Type Body { get; } = body;

    /// <summary>
    /// The response's <c>description</c>, or null for the status's standard reason phrase.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// A property on the declaration whose written value replaces <see cref="Status"/>.
    /// </summary>
    /// <remarks>
    /// For a declaration whose status is the author's to choose: <c>[Timeout]</c> answers 504 and
    /// <c>[Timeout(Status = 503)]</c> answers 503, and the document has to follow the operation
    /// rather than the default. Null where the status is fixed, which is the ordinary case.
    /// </remarks>
    public string? StatusFrom { get; set; }
}
