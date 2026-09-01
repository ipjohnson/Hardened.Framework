using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Abstract.Execution;

public interface IExecutionRequestHandlerInfo {
    string Path { get; }

    string Method { get; }

    Type HandlerType { get; }

    string InvokeMethod { get; }

    /// <summary>
    /// The status a successful response carries, or null for 200.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set from whichever front end declared it: a description's <c>responses:</c> key for a
    /// specification-first handler, <c>[Post(SuccessStatus = 201)]</c> for a hand-written one. Both
    /// arrive through <c>RequestHandlerModel.ResponseInformation.DefaultStatusCode</c>, so there is
    /// one runtime behaviour rather than two.
    /// </para>
    /// <para>
    /// This and the two below were dead until 2026-08-20 - declared here, never set, never read. The
    /// attributes that would have carried them were removed rather than wired in August, on the
    /// grounds that nothing read them; the reason nothing could was that a hand-written handler
    /// asserting a status has no source of truth behind it, and a described one does.
    /// </para>
    /// </remarks>
    int? SuccessStatus => null;

    int? FailureStatus => null;

    /// <summary>
    /// The status a null return carries, or null for the method-based default.
    /// </summary>
    /// <remarks>
    /// Null means 404 for GET and PUT, 200 for POST and DELETE - see
    /// <c>NullValueResponseHandler</c>. It is not derived from the description: null means the
    /// handler found nothing, which is 404, and reading it off whatever error an operation happens
    /// to declare would have the framework assert something the handler never said.
    /// </remarks>
    int? NullResponseStatus => null;

    /// <summary>
    /// What a null return writes as its body, or null to write nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generated instance of the schema the description declared for that status, holding the
    /// status and its reason phrase and nothing else. Shaped as the contract says so a client
    /// generated from the same document can read it; generic in content so it reveals nothing about
    /// why the handler found nothing.
    /// </para>
    /// <para>
    /// A handler that wants to say more throws the declared exception type instead, which carries a
    /// body it wrote. That is the division: return null when there is nothing to say, throw when
    /// there is.
    /// </para>
    /// </remarks>
    object? NullResponseBody => null;

    /// <summary>
    /// The media types this operation can produce, in the server's preference order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From a described operation's <c>content:</c> keys or a hand-written
    /// <c>[SupportedContentTypes]</c>. The first entry is what <c>Accept: */*</c> - or a request
    /// carrying no <c>Accept</c> at all - is answered with, because the first representation a
    /// document lists is the one it leads with.
    /// </para>
    /// <para>
    /// Empty means the operation said nothing, and negotiation behaves exactly as it did before any
    /// of this: every registered serializer is asked, and <c>*/*</c> takes whichever answers first.
    /// That is not the same as an empty set, which would mean the operation produces nothing.
    /// </para>
    /// </remarks>
    IReadOnlyList<string> ProducedContentTypes => Array.Empty<string>();

    /// <summary>
    /// The identifier of the parameter bound from the request body, or null when the handler
    /// takes none.
    /// </summary>
    /// <remarks>
    /// Carried so a failure inside deserialization can name its fields with the same prefix the
    /// generated validators use. The converter hardcoded <c>body</c>, which is right only where
    /// the parameter happens to be called that - so the filter and the deserializer disagreed
    /// about the same member's path on any handler that named it something else.
    /// </remarks>
    string? BodyParameterName => null;

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
