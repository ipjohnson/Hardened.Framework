using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class IoFilter : IExecutionFilter {
    private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
    private readonly Func<IExecutionContext, Task> _serializeResponse;
    private readonly Action<IExecutionContext>? _headerActions;

    public IoFilter(Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        Func<IExecutionContext, Task> serializeResponse,
        Action<IExecutionContext>? headerActions) {
        _deserializeRequest = deserializeRequest;
        _serializeResponse = serializeResponse;
        _headerActions = headerActions;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        // A request that is already decided does not have its body read.
        //
        // Nothing ahead of this filter could fail before authorization existed, so this was
        // previously unreachable and the body was read unconditionally. It is reachable now: a
        // requirement over grants alone is settled before serialization, and the entire reason for
        // putting it there is that a request presenting no credential must not cost a 10 MB
        // deserialization before it is rejected. Reading the body here would give that position
        // back for nothing.
        //
        // No bind duration is recorded either, because no bind was attempted - a zero would read as
        // a very fast deserialization rather than none.
        if (context.Response.ExceptionValue == null) {
            var bindParameterStartTimestamp = MachineTimestamp.Now;

            try {
                if (context.Request.Parameters == null) {
                    context.Request.Parameters = await _deserializeRequest(chain.Context);
                }
            }
            catch (Exception exp) {
                chain.Context.RequestServices.GetRequiredService<IRequestLogger>()
                    .RequestParameterBindFailed(chain.Context, exp);

                chain.Context.Response.ExceptionValue = exp;
            }
            finally {
                context.RequestMetrics.Record(RequestMetrics.ParameterBindDuration,
                    bindParameterStartTimestamp.GetElapsedMilliseconds());
            }
        }

        if (chain.Context.Response.ExceptionValue == null) {
            try {
                await chain.Next();
            }
            catch (Exception exp) {
                chain.Context.Response.ExceptionValue = exp;
            }
        }

        var responseTimestamp = MachineTimestamp.Now;

        try {
            _headerActions?.Invoke(chain.Context);

            if (chain.Context.Response.ShouldSerialize) {
                await _serializeResponse(chain.Context);
            }
        }
        finally {
            context.RequestMetrics.Record(RequestMetrics.ResponseDuration, responseTimestamp.GetElapsedMilliseconds());
        }
    }
}