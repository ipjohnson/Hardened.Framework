using Hardened.Amz.Function.Lambda.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Lambda.Testing;

public class TestLambdaInvokeFilterProvider: ILambdaInvokeFilterProvider
{
    public IExecutionFilter ProvideFilter(IServiceProvider serviceProvider)
    {
        return new TestLambdaFunctionInvokeFilter(
            serviceProvider.GetRequiredService<ILambdaContextAccessor>(),
            serviceProvider.GetServices<ILambdaHandlerPackage>());
    }
}