using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;

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

                try
                {
                    _invoke(chain.Context, controller, parameter);
                }
                catch (Exception e)
                {
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
}
