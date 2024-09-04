using Hardened.Amz.Function.Sqs.Runtime.Impl;
using Hardened.Amz.Shared.Lambda.Runtime.Filter;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime;

[HardenedModule]
public partial class SqsProcessing {

    public void RegisterDependencies(IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IBatchProcessorExceptionHandler, BatchProcessorExceptionHandler>();
    }
}