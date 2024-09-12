using Hardened.Amz.Cdk.Commands;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Cdk;

[HardenedModule]
public partial class HardenedCdk {
    private void RegisterDependencies(IServiceCollection services) {
        services.AddTransient<ICdkConfigurationRegistry>(
            scope => scope.GetRequiredService<CdkConfigurationRegistry>());
    }
}