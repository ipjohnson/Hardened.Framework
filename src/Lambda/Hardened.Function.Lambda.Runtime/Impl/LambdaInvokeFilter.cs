using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public class LambdaInvokeFilter : IExecutionFilter
    {
        private readonly IExecutionRequestHandler _executionRequestHandler;
        
        public LambdaInvokeFilter(IExecutionRequestHandler executionRequestHandler)
        {
            _executionRequestHandler = executionRequestHandler;
        }

        public async Task Execute(IExecutionChain chain)
        {
            await _executionRequestHandler.GetExecutionChain(chain.Context).Next();
        }
    }
}
