using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Lambda.Runtime.Execution;

namespace Hardened.Function.Lambda.Runtime.Impl
{
    public sealed class LambdaFunctionInvokeFilter : IExecutionFilter
    {
        private readonly ILambdaContextAccessor _lambdaContextAccessor;
        private readonly IEnumerable<ILambdaHandlerPackage> _handlerPackages;
        private IExecutionRequestHandler? _executionRequestHandler;

        public LambdaFunctionInvokeFilter(ILambdaContextAccessor lambdaContextAccessor, IEnumerable<ILambdaHandlerPackage> handlerPackages)
        {
            _lambdaContextAccessor = lambdaContextAccessor;
            _handlerPackages = handlerPackages;
        }

        public Task Execute(IExecutionChain chain)
        {
            if (_executionRequestHandler == null)
            {
                _executionRequestHandler = FindRequestHandler(chain.Context.RootServiceProvider);
            }

            return _executionRequestHandler.GetExecutionChain(chain.Context).Next();
        }

        private IExecutionRequestHandler FindRequestHandler(IServiceProvider serviceProvider)
        {
            foreach (var handlerPackage in _handlerPackages)
            {
                var handler = handlerPackage.GetFunctionHandler(serviceProvider, _lambdaContextAccessor.Context!);

                if (handler != null)
                {
                    return handler;
                }
            }

            throw new Exception("Could not find lambda function in package: " +
                                _lambdaContextAccessor.Context!.FunctionName);
        }
    }
}
