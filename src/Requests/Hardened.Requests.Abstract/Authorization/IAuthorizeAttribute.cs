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
public interface IAuthorizeAttribute {
    /// <summary>
    /// What this attribute requires of the caller.
    /// </summary>
    Requirement Requirement { get; }
}
