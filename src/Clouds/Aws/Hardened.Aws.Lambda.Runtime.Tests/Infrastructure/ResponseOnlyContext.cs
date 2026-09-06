using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Diagnostics;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// A context that answers for its response and refuses everything else.
/// </summary>
/// <remarks>
/// <c>IPayloadAdapter.WriteResponse</c> takes the whole context rather than the response, because a
/// batched source writes a per-record failure report and needs more than the response to do it. An
/// adapter that writes only the response should therefore touch only the response, and this is what
/// says so: a member it reaches for that it has no business reading throws here rather than
/// returning something plausible.
/// </remarks>
public sealed class ResponseOnlyContext : IExecutionContext {
    public ResponseOnlyContext(IExecutionResponse response) {
        Response = response;
    }

    public IExecutionResponse Response { get; }

    public IExecutionContext Clone(
        IExecutionRequest? request = null,
        IExecutionResponse? response = null,
        IServiceProvider? serviceProvider = null,
        IMetricLogger? metricLogger = null) => throw new NotSupportedException();

    public IServiceProvider RootServiceProvider => throw new NotSupportedException();
    public IKnownServices KnownServices => throw new NotSupportedException();
    public IServiceProvider RequestServices => throw new NotSupportedException();
    public IExecutionRequest Request => throw new NotSupportedException();
    public IMetricLogger RequestMetrics => throw new NotSupportedException();
    public MachineTimestamp StartTime => throw new NotSupportedException();
    public CancellationToken CancellationToken => throw new NotSupportedException();
    public string CorrelationId => throw new NotSupportedException();

    public ICallerPrincipal CallerPrincipal {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public object? HandlerInstance {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public IExecutionRequestHandlerInfo? HandlerInfo {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public DefaultOutputFunc? DefaultOutput {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}
