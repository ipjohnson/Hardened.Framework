using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.Lambda.Testing;

public class TestLambdaFunctionInvokeFilter : IExecutionFilter
{
    private readonly ILambdaContextAccessor _lambdaContextAccessor;
    private readonly IEnumerable<ILambdaHandlerPackage> _handlerPackages;

    public TestLambdaFunctionInvokeFilter(
        ILambdaContextAccessor lambdaContextAccessor, IEnumerable<ILambdaHandlerPackage> handlerPackages)
    {
        _lambdaContextAccessor = lambdaContextAccessor;
        _handlerPackages = handlerPackages;
    }

    public Task Execute(IExecutionChain chain)
    {
        var handler =  FindRequestHandler(chain.Context.RootServiceProvider);
        
        return handler.GetExecutionChain(chain.Context).Next();
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