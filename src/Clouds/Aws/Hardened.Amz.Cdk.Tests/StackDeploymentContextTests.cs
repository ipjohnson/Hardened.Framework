using Hardened.Amz.Cdk.Commands;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The deployment context is the only thing stacks share. One stack calls <c>Set</c> as it deploys
/// a resource and a later one calls <c>Get</c> to reach it, which is what makes deployment order a
/// correctness question rather than a preference.
/// </summary>
public class StackDeploymentContextTests {

    private static readonly CdkResourceRef<string> Table = new("orders-table");

    [Fact]
    public void AResourceComesBackOutUnderTheReferenceItWentInWith() {
        var context = Context();

        context.Set(Table, "arn:table");

        Assert.Equal("arn:table", context.Get(Table));
    }

    /// <summary>
    /// Value equality on the reference, not reference equality: the producing stack and the
    /// consuming stack each write their own <c>new CdkResourceRef&lt;T&gt;("name")</c>.
    /// </summary>
    [Fact]
    public void ASeparatelyConstructedReferenceToTheSameResourceFindsIt() {
        var context = Context();

        context.Set(new CdkResourceRef<string>("orders-table"), "arn:table");

        Assert.Equal("arn:table", context.Get(new CdkResourceRef<string>("orders-table")));
    }

    /// <summary>
    /// Two stacks deploying the same resource is a mistake, and a silent second write would leave
    /// consumers holding whichever one happened to run last.
    /// </summary>
    [Fact]
    public void DeployingTheSameResourceTwiceIsRejectedByName() {
        var context = Context();

        context.Set(Table, "first");

        var error = Assert.Throws<Exception>(() => context.Set(Table, "second"));

        Assert.Contains("orders-table", error.Message);
        Assert.Contains("already been deployed", error.Message);
    }

    /// <summary>
    /// This is the failure the deployment order exists to prevent: a consumer running before its
    /// producer sees a resource nothing has set.
    /// </summary>
    [Fact]
    public void AskingForAResourceNoStackHasDeployedFails() {
        Assert.ThrowsAny<Exception>(() => Context().Get(Table));
    }

    /// <summary>
    /// A stack may deploy a resource conditionally and record the decision by setting null. Asking
    /// for it then is a different mistake from asking for one that was never mentioned, and it says
    /// so.
    /// </summary>
    [Fact]
    public void AResourceRecordedAsNotDeployedSaysSoWhenAskedFor() {
        var context = Context();

        context.Set(Table, null);

        var error = Assert.Throws<Exception>(() => context.Get(Table));

        Assert.Contains("orders-table", error.Message);
        Assert.Contains("has not been deployed", error.Message);
    }

    [Fact]
    public void AnOptionalResourceThatWasDeployedComesBack() {
        var context = Context();

        context.Set(Table, "arn:table");

        Assert.Equal("arn:table", context.GetNullable(Table));
    }

    [Fact]
    public void AnOptionalResourceNoStackDeployedIsNull() {
        Assert.Null(Context().GetNullable(Table));
    }

    [Fact]
    public void AnOptionalResourceRecordedAsNotDeployedIsNull() {
        var context = Context();

        context.Set(Table, null);

        Assert.Null(context.GetNullable(Table));
    }

    /// <summary>
    /// The name is separate from the reference because it is the deployed resource's name — what a
    /// later stack puts in an alarm or a permission — while the reference is only how stacks agree
    /// on which resource they mean.
    /// </summary>
    [Fact]
    public void EachResourceKeepsTheDeployedNameItWasSetUnder() {
        var context = Context();

        context.Set(Table, "arn:table", "orders");

        Assert.Equal("orders", context.GetName(Table));
    }

    [Fact]
    public void AResourceSetWithoutADeployedNameHasAnEmptyOne() {
        var context = Context();

        context.Set(Table, "arn:table");

        Assert.Equal("", context.GetName(Table));
    }

    /// <summary>
    /// <c>Resources</c> is how a stack finds work to do without naming any of it — it is what
    /// <c>DeploymentGroupStack</c> reads to discover every alias deployed before it.
    /// </summary>
    [Fact]
    public void ResourcesListsEverythingDeployedSoFarWithItsName() {
        var context = Context();

        context.Set(new CdkResourceRef<string>("a"), "first-value", "first");
        context.Set(new CdkResourceRef<string>("b"), "second-value", "second");

        var deployed = context.Resources
            .Select(resource => ((string?)resource.Item1, resource.Item2))
            .OrderBy(resource => resource.Item2)
            .ToArray();

        Assert.Equal([("first-value", "first"), ("second-value", "second")], deployed);
    }

    [Fact]
    public void TheStageAndRegionComeFromTheConfigurationTheDeploymentWasRegisteredWith() {
        var context = Context(new TestStageConfiguration(KnownRegion.UsWest2, StageType.Gamma));

        Assert.Equal("gamma", context.Stage.StageName);
        Assert.Equal("us-west-2", context.SupportedRegion.Name);
    }

    [Fact]
    public void TheDeploymentNameIsTheOneTheApplicationRegistered() {
        Assert.Equal("orders-service", Context().DeploymentName);
    }

    [Fact]
    public void TheConfigurationIsReadableWithoutKnowingItsType() {
        var configuration = new TestStageConfiguration(KnownRegion.UsEast1, StageType.Dev);

        Assert.Same(configuration, Context(configuration).ConfigValue);
    }

    /// <summary>
    /// A value provider is how a deployment supplies something the stage configuration does not
    /// carry — a secret arn, a peered vpc id — resolved per stage and region rather than written
    /// once per environment.
    /// </summary>
    [Fact]
    public void AProvidedConfigValueIsResolvedForTheStageAndRegionBeingDeployed() {
        var provider = new RecordingValueProvider();
        var services = new ServiceCollection();
        services.AddSingleton<ICdkConfigurationValueProvider<string>>(provider);

        var context = Context(
            new TestStageConfiguration(KnownRegion.UsWest1, StageType.Production),
            services.BuildServiceProvider());

        Assert.Equal("provided", context.GetProvidedConfigValue<string>());
        Assert.Equal("production", provider.Stage?.StageName);
        Assert.Equal("us-west-1", provider.Region?.Name);
    }

    [Fact]
    public void AskingForAConfigValueNothingProvidesFails() {
        Assert.Throws<InvalidOperationException>(() => Context().GetProvidedConfigValue<string>());
    }

    private static StackDeploymentContext<TestStageConfiguration, StageType, KnownRegion> Context(
        TestStageConfiguration? configuration = null,
        IServiceProvider? serviceProvider = null) =>
        new("orders-service",
            configuration ?? new TestStageConfiguration(KnownRegion.UsEast1, StageType.Dev),
            serviceProvider ?? new ServiceCollection().BuildServiceProvider());

    private sealed class RecordingValueProvider : ICdkConfigurationValueProvider<string> {
        public IStageType? Stage { get; private set; }

        public ISupportedRegion? Region { get; private set; }

        public string ProvideValue(IStageType stageType, ISupportedRegion region) {
            Stage = stageType;
            Region = region;

            return "provided";
        }
    }
}
