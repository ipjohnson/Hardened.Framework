# CDK

`Hardened.Amz.Cdk` builds a CDK application out of the same modules and dependency injection as the
application it deploys, so infrastructure and runtime read the same configuration and share the same
notion of a stage and a region.

**Source:** [`src/Hardened.Amz.Cdk`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Hardened.Amz.Cdk)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

::: warning This package is less settled than the rest
The Lambda runtimes and clients are in production use; the CDK layer is younger and its surface is
still moving. Read [the source](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Hardened.Amz.Cdk)
alongside this page.
:::

## The deployment application

A deployment is a console application importing `[HardenedCdk]`. It is handed the
CDK `App` through the environment's custom data:

```csharp
using Amazon.CDK;
using Hardened.Amz.Cdk;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[HardenedCdk]
public partial class Deployment { }
```

```csharp
var cdkApp = new App();

var application = new Deployment(
    new EnvironmentImpl(
        arguments: args,
        customData: new Dictionary<string, object> { ["cdkApp"] = cdkApp }));

return await application.Run();
```

The deploy command resolves the configuration, orders the stacks, creates each `Stack` and calls
`Synth()`.

## Stages and regions

Stages and regions are types rather than strings, so a configuration cannot name a stage that does
not exist:

```csharp
public record StageType(string StageName, bool IsProduction = false) : IStageType {
    public static StageType Dev        = new("dev");
    public static StageType Beta       = new("beta");
    public static StageType Gamma      = new("gamma");
    public static StageType Production => new("production", true);
}
```

```csharp
public class KnownRegion(string name) : ISupportedRegion {
    public static KnownRegion UsEast1 => new("us-east-1");
    public static KnownRegion UsEast2 => new("us-east-2");
    public static KnownRegion UsWest1 => new("us-west-1");
    public static KnownRegion UsWest2 => new("us-west-2");
}
```

`IsProduction` is on the stage rather than inferred from its name, so a stack asks "is this
production?" without a string comparison.

An `IStageConfiguration<TRegion, TStage>` pairs the two, and is the value each stack definition
receives.

## Providing configuration

Implement `ICdkConfigurationProvider`. It is called with the stage and region being deployed, and
registers the configuration for them:

```csharp
public interface ICdkConfigurationProvider {
    void ProvideConfiguration(string stageType, string region, ICdkConfigurationRegistry registry);
}
```

```csharp
[SingletonService(As = typeof(ICdkConfigurationProvider))]
public class OrdersConfiguration : ICdkConfigurationProvider {
    public void ProvideConfiguration(
        string stageType, string region, ICdkConfigurationRegistry registry) {

        registry.RegisterConfiguration<OrdersConfig, StageType, KnownRegion>(
            "orders",
            new OrdersConfig(KnownRegion.UsWest2, StageType.Production) {
                TableReadCapacity = stageType == "production" ? 100 : 5
            });
    }
}
```

Without a provider, the deploy command fails with `"No ICdkConfigurationProvider exposed, please
implement."` rather than deploying something defaulted.

## Stack definitions

A stack definition declares what it produces and what it consumes, and the deploy command topologically
sorts them: a definition consuming something another produces is deployed after it. `Order` breaks
ties for stacks with no dependency between them.

`ShouldDeploy()` lets a definition opt out for a stage, which is how a stack that only exists in
production stays out of the dev account.

Inside a definition, `IStackDeploymentContext` is the shared state:

```csharp
public interface IStackDeploymentContext {
    IServiceProvider ServiceProvider { get; }
    string DeploymentName { get; }
    IStageType Stage { get; }
    ISupportedRegion SupportedRegion { get; }
    Stack Stack { get; set; }

    T Get<T>(CdkResourceRef<T> resource);
    T? GetNullable<T>(CdkResourceRef<T> resource);
    void Set<T>(CdkResourceRef<T> resource, T? value, string name = "");
    string GetName(ICdkResourceRef resource);
}
```

A `CdkResourceRef<T>` is a typed handle to a resource one stack creates and another needs. The
producing stack sets it; consuming stacks `Get` it, and a consumer that expects a table and is
handed a queue does not compile.

## Lambda defaults

`IDefaultFunctionProps` applies defaults to every function the deployment creates, so memory, timeout
and log retention are set in one place:

```csharp
[SingletonService(As = typeof(IDefaultFunctionProps))]
public class FunctionDefaults : IDefaultFunctionProps {
    public void ApplyDefaults(FunctionProps props) {
        props.MemorySize = 1024;
        props.Timeout = Duration.Seconds(30);
    }
}
```

## Running a deployment

The application is a console application, so CDK drives it the way it drives any synth:

```json
{
  "app": "dotnet run --project ./deploy/Orders.Deployment"
}
```

```
$ cdk deploy --all
```
