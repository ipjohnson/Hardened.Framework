using Amazon.CDK;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The stage configuration a deployment is registered with. Deliberately a class of the test's own
/// rather than <see cref="StageConfiguration"/>: the account is looked up by reflecting over the
/// configuration for a string property named after the account type, so a configuration without one
/// cannot deploy anything.
/// </summary>
public class TestStageConfiguration(KnownRegion region, StageType stage)
    : IStageConfiguration<KnownRegion, StageType> {

    public KnownRegion Region { get; } = region;

    public StageType Stage { get; } = stage;

    /// <summary>The default <c>AccountType</c> every stack definition asks for.</summary>
    public string ServiceAccount { get; init; } = "111122223333";
}

/// <summary>
/// A stack definition that records the order it deployed in and what the context looked like when
/// it did — which is the observable a deployment ordering test is actually about.
/// </summary>
public class RecordingStackDefinition(string name, List<string> deployLog)
    : IStackDefinition<TestStageConfiguration> {

    public string Name => name;

    public int Order { get; init; }

    public IEnumerable<ICdkResourceRef> Produces { get; init; } = [];

    public IEnumerable<ICdkResourceRef> Consumes { get; init; } = [];

    public bool Deployable { get; init; } = true;

    /// <summary>The CloudFormation stack the deploy command handed this definition.</summary>
    public Stack? DeployedInto { get; private set; }

    public bool ShouldDeploy(IStackDeploymentContext<TestStageConfiguration> context) => Deployable;

    public void Deploy(IStackDeploymentContext<TestStageConfiguration> context) {
        DeployedInto = context.Stack;
        deployLog.Add(name);

        foreach (var produced in Produces) {
            context.Set(new CdkResourceRef<string>(produced.Name), name, name);
        }
    }
}

/// <summary>
/// The non-generic stack definition shape — deployed alongside the typed ones, from the container
/// rather than from the registered configuration.
/// </summary>
public class UntypedStackDefinition(string name, List<string> deployLog) : IStackDefinition {

    public string Name => name;

    public int Order { get; init; }

    public void Deploy(IStackDeploymentContext context) => deployLog.Add(name);
}

/// <summary>
/// Stands in for the application's own provider — the thing a consumer implements to say which
/// configuration a given stage and region deploy with.
/// </summary>
public class TestConfigurationProvider(TestStageConfiguration configuration, string deploymentName = "orders")
    : ICdkConfigurationProvider {

    public string? Stage { get; private set; }

    public string? Region { get; private set; }

    public void ProvideConfiguration(string stageType, string region, ICdkConfigurationRegistry registry) {
        Stage = stageType;
        Region = region;

        registry.RegisterConfiguration<TestStageConfiguration, StageType, KnownRegion>(
            deploymentName, configuration);
    }
}

/// <summary>A configuration provider that registers nothing, which is its own kind of mistake.</summary>
public class EmptyConfigurationProvider : ICdkConfigurationProvider {
    public void ProvideConfiguration(string stageType, string region, ICdkConfigurationRegistry registry) { }
}
