using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Runtime.Errors;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Execution
{
    public class InvokeWithParametersFilter<TController, TParameter> : IExecutionFilter
    {
        private readonly ExecutionHelper.InvokeWithParameters<TController, TParameter> _invoke;

        public InvokeWithParametersFilter(ExecutionHelper.InvokeWithParameters<TController, TParameter> invoke)
        {
            _invoke = invoke;
        }

        public Task Execute(IExecutionChain chain)
        {
            var context = chain.Context;
            var startTimestamp = MachineTimestamp.Now;

            try
            {
                if (context.HandlerInstance is not TController controller)
                {
                    throw new Exception($"HandlerInstance was not instance of {typeof(TController)}");
                }

                if (context.Request.Parameters is not TParameter parameter)
                {
                    throw new Exception($"Parameters was not instance of {typeof(TParameter)}");
                }
                
                _invoke(chain.Context, controller, parameter);

                context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration, startTimestamp.GetElapsedMilliseconds());
            }
            catch (Exception e)
            {
                context.RequestMetrics.Record(RequestMetrics.HandlerInvokeDuration, startTimestamp.GetElapsedMilliseconds());

                return ControllerErrorHelper.HandleException(context, e);
            }

            return Task.CompletedTask;
        }
    }
}
