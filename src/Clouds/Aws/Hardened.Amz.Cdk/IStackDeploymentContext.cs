using Amazon.CDK;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk;

public interface ICdkResourceRef {
    string Name { get; }

    Type TypeOfResource { get; }
}

public enum DeploymentAccount {
    Service,
    Test
}

public record CdkResourceRef<T>(string Name = "default") : ICdkResourceRef {
    public Type TypeOfResource => typeof(T);
}

public interface IStackDeploymentContext {
    string DeploymentName { get; }

    IStageType Stage {
        get;
    }

    ISupportedRegion SupportedRegion { get; }

    Stack Stack { get; set; }

    Dictionary<ICdkResourceRef, Tuple<object?, string>>.ValueCollection Resources { get; }

    T Get<T>(CdkResourceRef<T> resource);

    string GetName(ICdkResourceRef resource);

    T? GetNullable<T>(CdkResourceRef<T> resource);

    void Set<T>(CdkResourceRef<T> resource, T? value, string name = "");
    
    object ConfigValue { get; }
}

public interface IStackDeploymentContext<TConfig> : IStackDeploymentContext {
    TConfig Value { get; }
}

public class StackDeploymentContext<TConfig, TStage, TRegion> : IStackDeploymentContext<TConfig>
    where TConfig : IStageConfiguration<TRegion, TStage>
    where TRegion : ISupportedRegion
    where TStage : IStageType {
    private Dictionary<ICdkResourceRef, Tuple<object?, string>> _resources = new();

    public string DeploymentName {
        get;
    }

    public IStageType Stage => Value.Stage;

    public ISupportedRegion SupportedRegion  => Value.Region;

    public Stack Stack { get; set; }

    public StackDeploymentContext(string deploymentName, TConfig value) {
        DeploymentName = deploymentName;
        Value = value;
    }

    public T Get<T>(CdkResourceRef<T> resource) {
        var value = _resources[resource];

        if (value.Item1 == null) {
            throw new Exception($"Resource {resource.Name} has not been deployed");
        }

        return (T)value.Item1;
    }

    public string GetName(ICdkResourceRef resource) {
        return _resources[resource].Item2;
    }

    public T? GetNullable<T>(CdkResourceRef<T> resource) {
        if (_resources.TryGetValue(resource, out var value)) {
            if (value.Item1 != null) {
                return (T)(object)value;
            }
        }
        return default;
    }

    public void Set<T>(CdkResourceRef<T> resource, T? value, string name = "") {
        if (_resources.ContainsKey(resource)) {
            throw new Exception($"Resource {resource.Name} has already been deployed");
        }

        _resources[resource] = new Tuple<object?, string>(value, name);
    }

    public object ConfigValue => Value;

    public Dictionary<ICdkResourceRef, Tuple<object?, string>>.ValueCollection Resources => _resources.Values;

    public TConfig Value { get; }
}

public interface IStackDefinitionBase {
    int Order => 0;

    string Name => "";

    string AccountType => "ServiceAccount";

    IEnumerable<ICdkResourceRef> Consumes => Enumerable.Empty<ICdkResourceRef>();

    IEnumerable<ICdkResourceRef> Produces => Enumerable.Empty<ICdkResourceRef>();
}

public interface IStackDefinitionDeployer {
    IStackDefinitionBase Definition { get; }

    bool ShouldDeploy();

    string Name();
    
    string AccountType();

    object ConfigValue();
    
    void Deploy();
}

public interface IStackDefinition : IStackDefinitionBase {

    string NameValue(IStackDeploymentContext context) => Name;

    bool ShouldDeploy(IStackDeploymentContext context) => true;

    void Deploy(IStackDeploymentContext context);
}

public interface IStackDefinition<T> : IStackDefinitionBase {

    string NameValue(IStackDeploymentContext<T> context) => Name;

    bool ShouldDeploy(IStackDeploymentContext<T> context) => true;

    void Deploy(IStackDeploymentContext<T> context);
}

[Expose]
[Singleton]
public class StackContextAccessor {
    public IStackDeploymentContext Context { get; set; } = null!;
}