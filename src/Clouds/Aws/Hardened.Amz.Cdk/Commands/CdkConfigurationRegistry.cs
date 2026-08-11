using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Cdk.Commands;

public interface ITypedStackDefinition {
    IStackDeploymentContext Context { get; }

    IEnumerable<IStackDefinitionDeployer> GetTypedDeployers(IServiceProvider serviceProvider);
}

// Registered as the concrete type, not as ICdkConfigurationRegistry: HardenedCdk maps the
// interface to this registration itself, and DeployCommandHandler injects the class.
[SingletonService(As = typeof(CdkConfigurationRegistry))]
public class CdkConfigurationRegistry : ICdkConfigurationRegistry {
    private readonly IServiceProvider _serviceProvider;

    public CdkConfigurationRegistry(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;

    }
    
    public void RegisterConfiguration<TConfig, TStage, TRegion>(string appName, TConfig config) 
        where TConfig : IStageConfiguration<TRegion, TStage> 
        where TStage : IStageType 
        where TRegion : ISupportedRegion {
        TypedStackDefinition = new TypedRegistryEntry<TConfig, TStage, TRegion>(appName, config, _serviceProvider);
    }
    
    public ITypedStackDefinition? TypedStackDefinition { get; private set; }
    
    public class TypedRegistryEntry<TConfig, TStage, TRegion> : ITypedStackDefinition
        where TConfig : IStageConfiguration<TRegion, TStage> 
        where TStage : IStageType 
        where TRegion : ISupportedRegion {
        private readonly StackDeploymentContext<TConfig, TStage, TRegion> _stackDeploymentContext;
        
        public TypedRegistryEntry(string appName, TConfig configuration, IServiceProvider serviceProvider) {
            _stackDeploymentContext = new StackDeploymentContext<TConfig, TStage, TRegion>(appName, configuration, serviceProvider);
        }
        
        public IStackDeploymentContext Context  => _stackDeploymentContext;

        public IEnumerable<IStackDefinitionDeployer> GetTypedDeployers(IServiceProvider serviceProvider) {
            return serviceProvider.GetRequiredService<IStackDefinitionProvider>().ProvideDeployers<TConfig, TStage, TRegion>(
                serviceProvider, _stackDeploymentContext);
        }
    }
    
    public class StackDefinitionDeployer<TConfig, TStage, TRegion> : IStackDefinitionDeployer
        where TConfig : IStageConfiguration<TRegion, TStage> 
        where TStage : IStageType 
        where TRegion : ISupportedRegion {
        private readonly IStackDefinition<TConfig> _stackDefinition;
        private readonly IStackDeploymentContext<TConfig> _stackDeploymentContext;

        public StackDefinitionDeployer(
            IStackDefinition<TConfig> stackDefinition, 
            IStackDeploymentContext<TConfig> stackDeploymentContext) {
            _stackDefinition = stackDefinition;
            _stackDeploymentContext = stackDeploymentContext;
        }

        public IStackDefinitionBase Definition  => _stackDefinition;

        public object ConfigValue() => _stackDeploymentContext.ConfigValue;

        public void Deploy() {
            _stackDefinition.Deploy(_stackDeploymentContext);
        }

        public bool ShouldDeploy() => _stackDefinition.ShouldDeploy(_stackDeploymentContext);
        
        public string Name() => _stackDefinition.NameValue(_stackDeploymentContext);
        
        public string AccountType() => _stackDefinition.AccountType;
    }
    
    public class StackDefinitionDeployer : IStackDefinitionDeployer {
        private readonly IStackDeploymentContext _stackDeploymentContext;
        private readonly IStackDefinition _definition;

        public StackDefinitionDeployer(
            IStackDeploymentContext stackDeploymentContext,
            IStackDefinition definition) {
            _stackDeploymentContext = stackDeploymentContext;
            _definition = definition;
        }
        
        public IStackDefinitionBase Definition  => _definition;

        public string AccountType() => _definition.AccountType;

        public object ConfigValue() => _stackDeploymentContext.ConfigValue;
        
        public void Deploy() {
            _definition.Deploy(_stackDeploymentContext);
        }
        
        public bool ShouldDeploy() => _definition.ShouldDeploy(_stackDeploymentContext);
        
        public string Name() => _definition.NameValue(_stackDeploymentContext);
    }
}

