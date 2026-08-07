using Hardened.Amz.Function.Sqs.Runtime.Impl;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime;

[Expose]
public class SqsStartup : IStartupService{

    public Task<bool> Startup(IServiceProvider rootProvider) {
        rootProvider.GetRequiredService<IGlobalFilterRegistry>().RegisterFilter(
            rootProvider.GetRequiredService<SqsBatchFilter>(), -1);
        
        return Task.FromResult(true);
    }
}