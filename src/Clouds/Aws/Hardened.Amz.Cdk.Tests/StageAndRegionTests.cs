using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The stage and region vocabulary a deployment is described in. It lives in
/// <c>Hardened.Amz.Shared.Lambda.Runtime</c> but the CDK package is what reads it: <c>IsProduction</c>
/// is the flag <see cref="Lambda.DeploymentGroupStack"/> turns into a rollout speed, and a region's
/// name is what ends up in the stack's environment.
/// </summary>
public class StageAndRegionTests {

    [Fact]
    public void ProductionIsTheOnlyStageThatIsProduction() {
        Assert.True(StageType.Production.IsProduction);
        Assert.False(StageType.Dev.IsProduction);
        Assert.False(StageType.Beta.IsProduction);
        Assert.False(StageType.Gamma.IsProduction);
    }

    /// <summary>
    /// A stage arrives as a string on the CDK command line — <c>-c stage=beta</c> — and is compared
    /// against these, so the case is part of the contract rather than a formatting choice.
    /// </summary>
    [Theory]
    [InlineData("dev")]
    [InlineData("beta")]
    [InlineData("gamma")]
    [InlineData("production")]
    public void StageNamesAreLowerCase(string expected) {
        var stages = new[] { StageType.Dev, StageType.Beta, StageType.Gamma, StageType.Production };

        Assert.Contains(stages, stage => stage.StageName == expected);
    }

    /// <summary>
    /// Anything not said to be production is not, so a deployment inventing its own stage gets the
    /// safe rollout rather than the fast one by default.
    /// </summary>
    [Fact]
    public void AStageIsNotProductionUnlessItSaysSo() {
        Assert.False(new StageType("loadtest").IsProduction);
    }

    [Fact]
    public void AStageThatSaysItIsProductionIsTreatedAsOne() {
        Assert.True(new StageType("prod-eu", IsProduction: true).IsProduction);
    }

    /// <summary>
    /// A configuration may name its stage rather than reuse the static, so two descriptions of the
    /// same stage have to be the same stage.
    /// </summary>
    [Fact]
    public void TwoDescriptionsOfTheSameStageAreEqual() {
        Assert.Equal(StageType.Production, new StageType("production", IsProduction: true));
    }

    /// <summary>
    /// Same name, different rollout behaviour, is not the same stage — the flag is the part that
    /// decides how a deployment rolls out.
    /// </summary>
    [Fact]
    public void AStageNamedTheSameButNotProductionIsADifferentStage() {
        Assert.NotEqual(StageType.Production, new StageType("production"));
    }

    [Theory]
    [InlineData("us-east-1")]
    [InlineData("us-east-2")]
    [InlineData("us-west-1")]
    [InlineData("us-west-2")]
    public void EveryKnownRegionCarriesItsAwsName(string expected) {
        var regions = new[] {
            KnownRegion.UsEast1, KnownRegion.UsEast2, KnownRegion.UsWest1, KnownRegion.UsWest2,
        };

        Assert.Contains(regions, region => region.Name == expected);
    }

    /// <summary>
    /// The known regions are a convenience, not the whole set. AWS has more regions than four and a
    /// deployment in one of them has to be describable.
    /// </summary>
    [Fact]
    public void ARegionOutsideTheKnownFourIsStillARegion() {
        ISupportedRegion region = new KnownRegion("eu-central-1");

        Assert.Equal("eu-central-1", region.Name);
    }

    [Fact]
    public void AStageConfigurationCarriesTheStageAndRegionItWasBuiltWith() {
        var configuration = new StageConfiguration(KnownRegion.UsWest2, StageType.Beta);

        Assert.Equal("us-west-2", configuration.Region.Name);
        Assert.Equal("beta", configuration.Stage.StageName);
    }

    [Fact]
    public void AStageDefinitionCarriesTheAccountRegionAndStageTogether() {
        var definition = new StageDefinition("111122223333", "us-east-1", StageType.Production);

        Assert.Equal("111122223333", definition.AccountId);
        Assert.Equal("us-east-1", definition.Region);
        Assert.True(definition.Stage.IsProduction);
    }
}
