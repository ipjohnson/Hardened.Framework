using Amazon.CDK.AWS.Lambda;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Lambda;

public interface IDefaultFunctionProps {
    void ApplyDefaults(FunctionProps funcProps);
}

[Expose]
public class DefaultFunctionProps : IDefaultFunctionProps {

    public void ApplyDefaults(FunctionProps funcProps) {
        
    }
}