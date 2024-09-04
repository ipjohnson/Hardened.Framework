using Hardened.Amz.Function.Sqs.Runtime;
using Hardened.Amz.Function.Sqs.Runtime.Attributes;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace SqsTest;

[HardenedModule]
[SqsProcessing.Module]
public partial class Application {
    public void RegisterDependencies(IEnvironment environment, IServiceCollection serviceCollection) {
        StandardDependencies.Register(environment, serviceCollection);
    }
}