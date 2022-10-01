using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Execution;

public class InvokeNoParametersFilter<TController> : IExecutionFilter
{
    private readonly ExecutionHelper.InvokeNoParameters<TController> _invoke;

    public InvokeNoParametersFilter(ExecutionHelper.InvokeNoParameters<TController> invoke)
    {
        _invoke = invoke;
    }

    public Task Execute(IExecutionChain chain)
    {
        var context = chain.Context;

        try
        {
            if (context.HandlerInstance is not TController controller)
            {
                throw new Exception($"HandlerInstance was not instance of {typeof(TController)}");
            }

            var startTimestamp = MachineTimestamp.Now;

            try
            {
                _invoke(chain.Context, controller);

                context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration, startTimestamp.GetElapsedMilliseconds());
            }
            catch (Exception e)
            {
                context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration, startTimestamp.GetElapsedMilliseconds());

                return ControllerErrorHelper.HandleException(context, e);
            }
        }
        catch (Exception e)
        {
            return ControllerErrorHelper.HandleException(context, e);
        }

        return Task.CompletedTask;
    }
}