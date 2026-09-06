using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.DDB.Runtime.Impl;

[TransientService]
public class FilterStartup : IStartupService {

    public Task<bool> Startup(IServiceProvider rootProvider) {
        rootProvider.GetRequiredService<IGlobalFilterRegistry>().RegisterFilter(
            rootProvider.GetRequiredService<DynamoDbExecutionFilter>(), -1);
        
        return Task.FromResult(true);
    }
}