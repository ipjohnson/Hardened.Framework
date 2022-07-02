using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Errors;

namespace Hardened.Requests.Runtime.Execution
{
    internal class AsyncInvokeNoParametersFilter<TController> : IExecutionFilter where TController : class
    {
        private readonly ExecutionHelper.AsyncInvokeNoParameters<TController> _invoke;

        public AsyncInvokeNoParametersFilter(ExecutionHelper.AsyncInvokeNoParameters<TController> invoke)
        {
            _invoke = invoke;
        }

        public async Task Execute(IExecutionChain chain)
        {
            var context = chain.Context;

            TController? controller = context.HandlerInstance as TController;

            try
            {
                if (controller == null)
                {
                    throw new Exception($"HandlerInstance is not an instance of {typeof(TController)}");
                }

                await _invoke(chain.Context, controller);
            }
            catch (Exception e)
            {
                await ControllerErrorHelper.HandleException(context, e);
            }
        }
    }
}
