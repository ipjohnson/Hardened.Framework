using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.Amz.Function.Lambda.Runtime;

/// <summary>
/// By default exceptions are serialized and returned,
/// this attribute will rethrow the original exception causing the lambda to fail
/// </summary>
public class ThrowExceptionAttribute : Attribute, IRequestFilterProvider, IExecutionFilter
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        yield return new RequestFilterInfo(
            _ => this,
            FilterOrder.BeforeSerialization
        );
    }

    public async Task Execute(IExecutionChain chain)
    {
        await chain.Next();

        if (chain.Context.Response.ExceptionValue != null)
        {
            throw chain.Context.Response.ExceptionValue;
        }
    }
}