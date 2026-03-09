using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Configuration;

public interface IDefaultResourceName {
    string Name { get; set; }
}

[Expose]
[SingletonService]
public class DefaultResourceName : IDefaultResourceName {

    public string Name { get; set; } = "";
}