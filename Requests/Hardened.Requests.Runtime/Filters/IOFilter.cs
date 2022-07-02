using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Filters
{
    public class IOFilter <T>: IExecutionFilter
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

            context.Request.Parameters = await _deserializeRequest(chain.Context);

            await chain.Next();

            await _serializeResponse(chain.Context);
        }
    }
}
