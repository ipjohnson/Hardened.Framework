using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Cdk.Commands;

public interface IStackDefinitionProvider {
    IEnumerable<IStackDefinitionDeployer> ProvideDeployers<TConfig, TStage, TRegion>(
        IServiceProvider serviceProvider,
        IStackDeploymentContext<TConfig> context)
        where TConfig : IStageConfiguration<TRegion, TStage>
        where TRegion : ISupportedRegion
        where TStage : IStageType;
}

[Expose]
public class StackDefinitionProvider : IStackDefinitionProvider {

    public IEnumerable<IStackDefinitionDeployer> ProvideDeployers<TConfig, TStage, TRegion>(
        IServiceProvider serviceProvider, 
        IStackDeploymentContext<TConfig> context) 
        where TConfig : IStageConfiguration<TRegion, TStage> 
        where TStage : IStageType 
        where TRegion : ISupportedRegion {
        
        var services = 
            serviceProvider.GetServices<IStackDefinition<TConfig>>();

        foreach (var stackDefinition in services) {
            yield return new CdkConfigurationRegistry.StackDefinitionDeployer<TConfig, TStage, TRegion>(
                stackDefinition, context);
        }
    }
}