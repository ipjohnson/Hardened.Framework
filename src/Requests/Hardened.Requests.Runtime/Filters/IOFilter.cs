using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Shared.Runtime.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters
{
    public class IOFilter : IExecutionFilter
    {
        private readonly Func<IExecutionContext, Task<IExecutionRequestParameters>> _deserializeRequest;
        private readonly Func<IExecutionContext, Task> _serializeResponse;
        private readonly Action<IExecutionContext>? _headerActions;

        public IOFilter(Func<IExecutionContext, Task<IExecutionRequestParameters>> deserializeRequest,
            Func<IExecutionContext, Task> serializeResponse, 
            Action<IExecutionContext>? headerActions)
        {
            _deserializeRequest = deserializeRequest;
            _serializeResponse = serializeResponse;
            _headerActions = headerActions;
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
                chain.Context.RequestServices.GetRequiredService<IRequestLogger>().RequestParameterBindFailed(chain.Context, exp);

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
                _headerActions?.Invoke(chain.Context);

                await _serializeResponse(chain.Context);
            }
            finally
            {
                context.RequestMetrics.Record(RequestMetrics.ResponseDuration, responseTimestamp.GetElapsedMilliseconds());
            }
        }
    }
}
