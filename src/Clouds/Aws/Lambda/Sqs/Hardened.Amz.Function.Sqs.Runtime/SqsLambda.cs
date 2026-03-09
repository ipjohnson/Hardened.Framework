using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.Sqs.Runtime;

[HardenedModule]
public partial class SqsLambda {

    public void RegisterDependencies(IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IBatchProcessorExceptionHandler, BatchProcessorExceptionHandler>();
    }
}