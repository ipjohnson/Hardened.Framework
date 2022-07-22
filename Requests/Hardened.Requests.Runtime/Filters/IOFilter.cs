using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Shared.Runtime.Diagnostics;

namespace Hardened.Requests.Runtime.Filters
{
    public class IOFilter : IExecutionFilter
    {
        private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
        private readonly Func<IExecutionContext, Task> _serializeResponse;

        public IOFilter(
            Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest, 
            Func<IExecutionContext, Task> serializeResponse)
        {
            _deserializeRequest = deserializeRequest;
            _serializeResponse = serializeResponse;
        }

        public async Task Execute(IExecutionChain chain)
        {
            var context = chain.Context;
            var bindParameterStartTimestamp = MachineTimestamp.Now;

            try
            {
                context.Request.Parameters = await _deserializeRequest(chain.Context);
            }
            catch (Exception exp)
            {
                chain.Context.Response.ExceptionValue = exp;
            }
            finally
            {
                context.RequestMetrics.Record(RequestMetrics.ParameterBindDuration, bindParameterStartTimestamp.GetElapsedMilliseconds());
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
            }

            var responseTimestamp = MachineTimestamp.Now;

            try
            {
                await _serializeResponse(chain.Context);
            }
            finally
            {
                context.RequestMetrics.Record(RequestMetrics.ResponseDuration, responseTimestamp.GetElapsedMilliseconds());
            }
        }
    }
}
