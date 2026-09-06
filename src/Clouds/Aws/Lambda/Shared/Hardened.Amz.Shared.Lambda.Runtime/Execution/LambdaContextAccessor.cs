using Amazon.Lambda.Core;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Shared.Lambda.Runtime.Execution;

public interface ILambdaContextAccessor {
    ILambdaContext? Context { get; set; }
}

[SingletonService(Using = RegistrationType.Try)]
public class LambdaContextAccessor : ILambdaContextAccessor {
    public ILambdaContext? Context { get; set; }

    public void T() { }
}