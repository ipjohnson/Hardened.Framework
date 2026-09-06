using Amazon.CDK.AWS.Lambda;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Lambda;

public interface IDefaultFunctionProps {
    void ApplyDefaults(FunctionProps funcProps);
}

[TransientService]
public class DefaultFunctionProps : IDefaultFunctionProps {

    public void ApplyDefaults(FunctionProps funcProps) {
        
    }
}