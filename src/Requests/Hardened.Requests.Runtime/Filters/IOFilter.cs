using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class IoFilter : IExecutionFilter, IBindingFilter
{
    private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
    private readonly Func<IExecutionContext, Task> _serializeResponse;
    private readonly Action<IExecutionContext>? _headerActions;

    public IoFilter(
        Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
        Func<IExecutionContext, Task> serializeResponse,
        Action<IExecutionContext>? headerActions
    )
    {
        _deserializeRequest = deserializeRequest;
        _serializeResponse = serializeResponse;
        _headerActions = headerActions;
    }

    IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> IBindingFilter.ConstraintFilters
    {
        set => _constraintFilters = value;
    }

    private IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> _constraintFilters =
        Array.Empty<Func<IExecutionContext, IExecutionFilter>>();

    public async Task Execute(IExecutionChain chain)
    {
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
        if (context.Response.ExceptionValue == null)
        {
            var bindParameterStartTimestamp = MachineTimestamp.Now;

            try
            {
                if (context.Request.Parameters == null)
                {
                    context.Request.Parameters = await _deserializeRequest(chain.Context);
                }
            }
            catch (Exception exp)
            {
                chain
                    .Context.RequestServices.GetRequiredService<IRequestLogger>()
                    .RequestParameterBindFailed(chain.Context, exp);

                chain.Context.Response.ExceptionValue = BindFailure.For(
                    exp,
                    chain.Context,
                    _constraintFilters
                );
            }
            finally
            {
                context.RequestMetrics.Record(
                    RequestMetrics.ParameterBindDuration,
                    bindParameterStartTimestamp.GetElapsedMilliseconds()
                );
            }
        }

        if (chain.Context.Response.ExceptionValue == null)
        {
            try
            {
                await chain.Next();
            }
            catch (Exception exp)
            {
                chain.Context.Response.ExceptionValue = exp;
            }

            // Whether the exception arrived here or the invoke filter caught it first.
            if (chain.Context.Response.ExceptionValue is { } failure)
            {
                Unanswered(chain.Context, failure);
            }
        }

        var responseTimestamp = MachineTimestamp.Now;

        try
        {
            _headerActions?.Invoke(chain.Context);

            if (chain.Context.Response.ShouldSerialize)
            {
                await _serializeResponse(chain.Context);

                // Answered. The flag reads as "this response still needs writing", which is what
                // lets ResponseFinalizerFilter cover a middleware that answered without ever
                // reaching a handler chain, and not write this one a second time on the way out.
                chain.Context.Response.ShouldSerialize = false;
            }
        }
        finally
        {
            context.RequestMetrics.Record(
                RequestMetrics.ResponseDuration,
                responseTimestamp.GetElapsedMilliseconds()
            );
        }
    }

    /// <summary>
    /// A handler that took over its body and then threw, which has not answered.
    /// </summary>
    /// <remarks>
    /// Clearing <c>ShouldSerialize</c> is how a handler says it writes the body itself, and the
    /// exception was then serialized nowhere and logged nowhere: the caller got the status the
    /// handler had set with an empty body. A health probe that failed its write answered 200 that
    /// way. Where nothing reached the wire the failure is answered as any other is. Where the body
    /// had started, the status can no longer change, and the failure is logged rather than lost.
    /// </remarks>
    private static void Unanswered(IExecutionContext context, Exception exp)
    {
        if (context.Response.ShouldSerialize)
        {
            return;
        }

        if (!context.Response.ResponseStarted)
        {
            context.Response.ShouldSerialize = true;

            return;
        }

        context.RequestServices.GetRequiredService<IRequestLogger>().RequestFailed(context, exp);
    }
}
