namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// An attribute that imposes a <see cref="Authorization.Requirement"/> on the handler it is written on.
/// </summary>
/// <remarks>
/// <para>
/// A handler's attributes reach the runtime as an <c>object[]</c> of metadata, so something has to
/// recognise the authorization ones among the rest. This is that something: one interface both
/// attribute forms implement, which is what lets the pipeline collect requirements without knowing
/// the closed type of a <c>[Authorize&lt;T&gt;]</c> - and without reflecting over an open generic,
/// which would not survive trimming.
/// </para>
/// <para>
/// Two forms implement it. <c>[Authorize&lt;T&gt;]</c> yields the policy's requirement;
/// <c>[AuthorizeGrants]</c> yields an ad-hoc conjunction, and repeating it yields a disjunction of
/// those. Both arrive here as one requirement each, and the pipeline combines them the same way
/// whichever they came from.
/// </para>
/// </remarks>
public interface IAuthorizeAttribute {
    /// <summary>
    /// What this attribute requires of the caller.
    /// </summary>
    Requirement Requirement { get; }
}
