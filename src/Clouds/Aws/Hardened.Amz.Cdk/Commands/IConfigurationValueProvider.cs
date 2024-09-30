using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Commands;

public interface IConfigurationValueProvider<T> {
    T ProvideValue(IStageType stageType, ISupportedRegion region);
}