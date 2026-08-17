using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionRequestHandlerInfo {
    string Path { get; }

    string Method { get; }

    Type HandlerType { get; }

    string InvokeMethod { get; }

    int? SuccessStatus => null;

    int? FailureStatus => null;

    int? NullResponseStatus => null;

    IReadOnlyList<IExecutionRequestParameter> Parameters { get; }

    IReadOnlyList<object> Metadata => Array.Empty<object>();

    /// <summary>
    /// What this handler requires of its caller, or null if nothing does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First-class data rather than something the authorization filter re-derives from
    /// <see cref="Metadata"/> on its own. Attributes are one source of it; a convention applied
    /// while this info was built is another, and a handler registered by hand can state one without
    /// inventing an attribute to carry it. One requirement, whatever contributed to it.
    /// </para>
    /// <para>
    /// <b>One rather than a list, because everything conjoins.</b> Contributions are combined with
    /// <see cref="Requirement.AllOf"/>, which flattens, so ten sources produce one node of ten
    /// rather than a chain - and a list would carry nothing the combined tree does not. A
    /// contribution can therefore only narrow what is admitted. Alternatives live inside a single
    /// requirement, never between two of them.
    /// </para>
    /// <para>
    /// Grants do not appear here. This is the whole authorization answer for the handler, expressed
    /// the one way the runtime evaluates it, and the grants it names are reachable through
    /// <see cref="Requirement.RequiredGrants"/> when a refusal has to say what it wanted.
    /// </para>
    /// <para>
    /// The default derives from <see cref="Metadata"/>, so an implementation that carries attributes
    /// and nothing else is already correct. <c>ExecutionRequestHandlerInfo</c> computes it once
    /// instead, since it is asked for every handler as its filter chain is built.
    /// </para>
    /// </remarks>
    Requirement? Requirement => RequirementFrom(Metadata);

    /// <summary>
    /// Conjoins the requirement of every authorization attribute in <paramref name="metadata"/>.
    /// </summary>
    /// <remarks>
    /// Shared with implementations that precompute, so both spellings mean the same thing rather
    /// than agreeing by inspection.
    /// </remarks>
    static Requirement? RequirementFrom(IReadOnlyList<object> metadata) {
        List<Requirement>? requirements = null;

        foreach (var item in metadata) {
            if (item is IAuthorizeAttribute authorize) {
                (requirements ??= []).Add(authorize.Requirement);
            }
        }

        return requirements == null ? null : Authorization.Requirement.AllOf([..requirements]);
    }
}
