using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk;

public interface ICdkConfigurationRegistry {
    void RegisterConfiguration<TConfig, TStage, TRegion>(string appName, TConfig config)
        where TConfig : IStageConfiguration<TRegion, TStage>
        where TRegion : ISupportedRegion
        where TStage : IStageType;
    
    
}

public interface ICdkConfigurationProvider {
    
    void ProvideConfiguration(string stageType, string region, ICdkConfigurationRegistry registry);
}