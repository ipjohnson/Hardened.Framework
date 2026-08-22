using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// The authorization a description declared, as a handler's generated code carries it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an attribute, deliberately.</b> A handler's metadata is an <c>object[]</c> and
/// <c>IExecutionRequestHandlerInfo.RequirementFrom</c> conjoins every
/// <see cref="IAuthorizeAttribute"/> it finds there, whatever the concrete type - so this
/// participates fully without being writable by hand. It could not be written by hand anyway: an
/// attribute argument must be a constant, and a <see cref="Requirement"/> is a tree.
/// </para>
/// <para>
/// <b>It composes rather than replaces.</b> Because it arrives as one more requirement among the
/// handler's own, a described requirement is conjoined with anything the implementation declared -
/// so a contract can narrow a route and cannot widen one. <c>[AllowAnonymous]</c> remains the single
/// thing that cancels it, which is the same rule an attribute or a convention is held to.
/// </para>
/// <para>
/// The alternative was passing the requirement to <c>ExecutionRequestHandlerInfo</c> directly, which
/// reads <c>requirement ?? RequirementFrom(Metadata)</c> - so a described requirement would have
/// <em>silenced</em> an <c>[AuthorizeGrants]</c> written on the implementation. Composition is the
/// whole point; that spelling would have inverted it.
/// </para>
/// </remarks>
public sealed class DescribedAuthorization : IAuthorizeAttribute {
    public DescribedAuthorization(Requirement requirement) {
        Requirement = requirement;
    }

    public Requirement Requirement { get; }
}
