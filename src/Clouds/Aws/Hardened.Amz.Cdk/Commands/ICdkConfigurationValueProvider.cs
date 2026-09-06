using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Commands;

public interface ICdkConfigurationValueProvider<T> {
    T ProvideValue(IStageType stageType, ISupportedRegion region);
}