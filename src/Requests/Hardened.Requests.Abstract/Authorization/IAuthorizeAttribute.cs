using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// An attribute that imposes a <see cref="Authorization.Requirement"/> on the handler it is written on.
/// </summary>
/// <remarks>
/// <para>
/// A handler's attributes reach the runtime as an <c>object[]</c> of metadata, so something has to
/// recognise the authorization ones among the rest. This is that something: one interface every
/// attribute form implements, which is what lets the pipeline collect requirements without knowing
/// the closed type of a <c>[Authorize&lt;T&gt;]</c> - and without reflecting over an open generic,
/// which would not survive trimming.
/// </para>
/// <para>
/// <b>Every implementation found on a handler is required.</b> The requirements are conjoined, so
/// an attribute can only ever narrow what is admitted. That holds however the attribute arrived -
/// written on the method, written on the controller, derived from another attribute, or added by a
/// convention - and it is what makes all four compose without a rule per case.
/// </para>
/// <para>
/// It is also why this is the interface a source generator matches on rather than a list of type
/// names. An interface is visible on a type from a referenced assembly; a constructor body is not.
/// </para>
/// </remarks>
// The 403 only. The document generator publishes the 401 for every operation carrying one of
// these, and for every operation a module-level requirement reaches, with its WWW-Authenticate
// challenge beside it. The 403 carries a challenge too, naming the scope the caller lacks. The
// generator skips both for [Authorize<TScheme>], which requires only an authenticated caller and
// so can only answer the 401.
[AnswersStatus(
    403,
    typeof(Errors.ErrorModel),
    Description = "The caller does not hold what this operation requires."
)]
[AnswersHeader(
    403,
    "WWW-Authenticate",
    Description = "The challenge naming what the caller lacks, as error=\"insufficient_scope\"."
)]
public interface IAuthorizeAttribute
{
    /// <summary>
    /// What this attribute requires of the caller.
    /// </summary>
    Requirement Requirement { get; }
}
