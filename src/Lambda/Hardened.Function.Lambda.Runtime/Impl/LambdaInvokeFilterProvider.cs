using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Lambda.Runtime.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Function.Lambda.Runtime.Impl;

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