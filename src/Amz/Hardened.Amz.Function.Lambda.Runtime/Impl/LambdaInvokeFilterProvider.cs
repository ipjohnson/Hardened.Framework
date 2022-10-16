using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Runtime.Impl;

public interface ILambdaInvokeFilterProvider
{
    IExecutionFilter ProvideFilter(IServiceProvider serviceProvider);
}

public class LambdaInvokeFilterProvider : ILambdaInvokeFilterProvider
{
    public IExecutionFilter ProvideFilter(IServiceProvider serviceProvider)
    {
        return new LambdaFunctionInvokeFilter(
            serviceProvider.GetRequiredService<ILambdaContextAccessor>(),
            serviceProvider.GetServices<ILambdaHandlerPackage>());
    }
}