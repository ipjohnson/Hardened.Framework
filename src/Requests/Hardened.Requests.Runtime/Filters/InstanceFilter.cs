using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Requests.Runtime.Filters;

public class InstanceFilter<TController> : IExecutionFilter {
    public Task Execute(IExecutionChain chain) {
        
        chain.Context.HandlerInstance = 
            chain.Context.RequestServices.GetRequiredService(typeof(TController));
        
        return chain.Next();
    }
}