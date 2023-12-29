using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Execution;

public class AsyncInvokeWithParametersFilter<TController, TParameter> : IExecutionFilter
    where TController : class where TParameter : class {
    private readonly ExecutionHelper.AsyncInvokeWithParameters<TController, TParameter> _invoke;

    public AsyncInvokeWithParametersFilter(ExecutionHelper.AsyncInvokeWithParameters<TController, TParameter> invoke) {
        _invoke = invoke;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var startTimestamp = MachineTimestamp.Now;

        try {
            if (context.HandlerInstance is not TController controller) {
                throw new Exception($"HandlerInstance is not an instance of {typeof(TController)}");
            }

            if (context.Request.Parameters is not TParameter parameter) {
                throw new Exception($"Parameter is not an instance of {typeof(TParameter)}");
            }

            await _invoke(context, controller, parameter);

            context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration,
                startTimestamp.GetElapsedMilliseconds());
        }
        catch (Exception e) {
            context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration,
                startTimestamp.GetElapsedMilliseconds());

            await ControllerErrorHelper.HandleException(context, e);
        }
    }
}