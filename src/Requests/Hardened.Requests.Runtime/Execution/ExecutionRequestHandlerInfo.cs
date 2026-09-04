using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Timeouts;

namespace Hardened.Requests.Runtime.Execution;

public class ExecutionRequestHandlerInfo : IExecutionRequestHandlerInfo {
    public ExecutionRequestHandlerInfo(
        string path,
        string method,
        Type handlerType,
        string invokeMethod,
        IReadOnlyList<IExecutionRequestParameter>? parameters = null,
        IReadOnlyList<object>? metadata = null,
        Requirement? requirement = null,
        int? successStatus = null,
        object? nullResponseBody = null,
        IReadOnlyList<string>? producedContentTypes = null,
        string? bodyParameterName = null,
        int? validationErrorStatus = null,
        TimeoutPolicy? timeout = null) {
        Path = path;
        Method = method;
        HandlerType = handlerType;
        InvokeMethod = invokeMethod;
        Parameters = parameters ?? new List<IExecutionRequestParameter>();
        Metadata = metadata ?? Array.Empty<object>();
        Requirement = requirement ?? IExecutionRequestHandlerInfo.RequirementFrom(Metadata);
        SuccessStatus = successStatus;
        NullResponseBody = nullResponseBody;
        ProducedContentTypes = producedContentTypes ?? Array.Empty<string>();
        BodyParameterName = bodyParameterName;
        ValidationErrorStatus = validationErrorStatus;
        Timeout = timeout ?? IExecutionRequestHandlerInfo.TimeoutFrom(Metadata);
    }

    /// <summary>
    /// A copy of <paramref name="source"/> with the named members replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place that enumerates every member of the interface. <see cref="WithPath"/> and
    /// <see cref="ExecutionRequestHandlerInfoExtensions.WithRequirement"/> both route through it, so
    /// a member added to <see cref="IExecutionRequestHandlerInfo"/> is carried by both amendments or
    /// by neither, rather than by whichever one somebody remembered.
    /// </para>
    /// <para>
    /// That is the defect this closes. Applying a convention used to rebuild the handler from seven
    /// of the ten arguments, dropping <see cref="SuccessStatus"/>, <see cref="NullResponseBody"/>
    /// and <see cref="ProducedContentTypes"/> on the floor - so a route declaring 201 answered 200
    /// as soon as any authorization convention was registered, from a contract that said otherwise,
    /// and nothing warned.
    /// </para>
    /// <para>
    /// A null override keeps what the source carried. <see cref="Requirement"/> falls back to the
    /// primary constructor's derivation from metadata, which is the same answer the source reached.
    /// </para>
    /// </remarks>
    internal ExecutionRequestHandlerInfo(
        IExecutionRequestHandlerInfo source,
        string? path,
        Requirement? requirement,
        TimeoutPolicy? timeout = null)
        : this(
            path ?? source.Path,
            source.Method,
            source.HandlerType,
            source.InvokeMethod,
            source.Parameters,
            source.Metadata,
            requirement ?? source.Requirement,
            source.SuccessStatus,
            source.NullResponseBody,
            source.ProducedContentTypes,
            source.BodyParameterName,
            source.ValidationErrorStatus,
            timeout ?? source.Timeout) { }

    public string Path { get; }

    public string Method { get; }

    public Type HandlerType { get; }

    public string InvokeMethod { get; }

    public IReadOnlyList<IExecutionRequestParameter> Parameters { get; }

    public IReadOnlyList<object> Metadata { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Computed once here rather than per read. The generated handler holds this in a static field,
    /// so the walk over metadata happens once per handler type for the life of the process.
    /// </remarks>
    public Requirement? Requirement { get; }

    /// <inheritdoc />
    public int? SuccessStatus { get; }

    /// <inheritdoc />
    public object? NullResponseBody { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> ProducedContentTypes { get; }

    /// <inheritdoc />
    public string? BodyParameterName { get; }

    /// <inheritdoc />
    public int? ValidationErrorStatus { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Computed once here rather than per read, as <see cref="Requirement"/> is. What the primary
    /// constructor derives is the operation and its class; the assembly and entry-point rungs and
    /// any convention are folded in by <c>ExecutionHelper</c>, which amends the resolved value on
    /// before anything downstream reads it.
    /// </remarks>
    public TimeoutPolicy? Timeout { get; }

    /// <summary>
    /// The same handler, addressed at <paramref name="path"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A handler class is generated from its own declarations, so the <see cref="Path"/> it is
    /// born with is the controller's <c>[BasePath]</c> and the route template - <c>/books</c>.
    /// The module's <c>[BasePath]</c> is not there to be seen: it belongs to the entry point, and
    /// is applied where the routing table is built. So a handler served at
    /// <c>/catalog/books</c> described itself as <c>/books</c>, and everything reading
    /// <c>IExecutionRequestHandlerInfo.Path</c> - a global filter registered per handler, an
    /// authorization convention, a log line - was given a path that matched no request the
    /// application would ever receive.
    /// </para>
    /// <para>
    /// The routing table calls this when it constructs a handler, passing the path it routed to.
    /// Composition stays in the generator, which already computes exactly this string for the
    /// route tree, the links and the OpenAPI document - so there is no second implementation of
    /// the base-path rules to disagree with the first.
    /// </para>
    /// <para>
    /// Returns <c>this</c> when there is nothing to change, so a handler under no module base
    /// path costs nothing.
    /// </para>
    /// </remarks>
    public ExecutionRequestHandlerInfo WithPath(string? path) =>
        string.IsNullOrEmpty(path) || string.Equals(path, Path, StringComparison.Ordinal)
            ? this
            : new ExecutionRequestHandlerInfo(this, path, requirement: null, timeout: null);
}

/// <summary>
/// Amendments to a handler that carry every other member across unchanged.
/// </summary>
public static class ExecutionRequestHandlerInfoExtensions {

    /// <summary>
    /// The same handler, requiring <paramref name="requirement"/> of its caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extension rather than an interface member, because the amendment has to work on whatever
    /// implementation reached it and the interface is one an application may implement. The result
    /// is a plain <see cref="ExecutionRequestHandlerInfo"/>: every member of the source is copied,
    /// so nothing an implementation answered differently is lost except the identity of the type
    /// that answered it.
    /// </para>
    /// <para>
    /// Returns <c>this</c> when there is nothing to change, so a handler no convention spoke about
    /// costs nothing.
    /// </para>
    /// </remarks>
    public static IExecutionRequestHandlerInfo WithRequirement(
        this IExecutionRequestHandlerInfo handlerInfo, Requirement? requirement) =>
        requirement is null || ReferenceEquals(requirement, handlerInfo.Requirement)
            ? handlerInfo
            : new ExecutionRequestHandlerInfo(handlerInfo, path: null, requirement);

    /// <summary>
    /// The same handler, bounded by <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// How the rungs a handler cannot answer alone - its assembly, the entry point's default, a
    /// convention - reach everything that reads the handler. Without it the filter would enforce
    /// one budget while <see cref="IExecutionRequestHandlerInfo.Timeout"/> reported another, which
    /// is the second, effective view the first-class property exists to prevent.
    ///
    /// <para>
    /// Returns the handler unchanged where the resolved policy is the one it already carried, so a
    /// handler no rung beyond its own metadata spoke about costs nothing.
    /// </para>
    /// </remarks>
    public static IExecutionRequestHandlerInfo WithTimeout(
        this IExecutionRequestHandlerInfo handlerInfo, TimeoutPolicy? timeout) =>
        timeout is null || Equals(timeout, handlerInfo.Timeout)
            ? handlerInfo
            : new ExecutionRequestHandlerInfo(handlerInfo, path: null, requirement: null, timeout);
}
