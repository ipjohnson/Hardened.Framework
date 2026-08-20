using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

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
        IReadOnlyList<string>? producedContentTypes = null) {
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
    }

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
            : new ExecutionRequestHandlerInfo(
                path!, Method, HandlerType, InvokeMethod, Parameters, Metadata, Requirement,
                SuccessStatus, NullResponseBody, ProducedContentTypes);
}
