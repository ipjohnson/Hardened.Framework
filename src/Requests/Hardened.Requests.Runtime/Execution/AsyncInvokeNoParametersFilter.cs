using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Execution;

internal class AsyncInvokeNoParametersFilter<TController> : IExecutionFilter where TController : class {
    private readonly ExecutionHelper.AsyncInvokeNoParameters<TController> _invoke;

    public AsyncInvokeNoParametersFilter(ExecutionHelper.AsyncInvokeNoParameters<TController> invoke) {
        _invoke = invoke;
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;
        var startTimestamp = MachineTimestamp.Now;

        try {
            if (context.HandlerInstance is not TController controller) {
                throw new Exception($"HandlerInstance is not an instance of {typeof(TController)}");
            }

            await _invoke(context, controller);

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