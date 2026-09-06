using Amazon.CDK.AWS.Lambda;
using Hardened.Amz.Cdk.Commands;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// Which stack deploys first. A stack reaches another stack's resource through the deployment
/// context, so a consumer running before its producer does not deploy a broken stack — it throws
/// part-way through a deployment, having already created whatever came before it.
/// </summary>
public class StackOrderingTests {

    private static readonly CdkResourceRef<Function> TheFunction = new();

    /// <summary>
    /// The behaviour the whole mechanism exists for.
    ///
    /// <para>
    /// Broken until 2026-08-11: the comparison returned 1 where it meant -1, so a producer sorted
    /// after every stack consuming what it produced. The consumer deployed first and its
    /// <c>context.Get</c> threw on a resource nothing had set yet. <c>Hardened.Amz.Cdk</c> shipped
    /// with no tests at all, which is how a reversed comparison stayed reversed.
    /// </para>
    /// </summary>
    [Fact]
    public void AProducerDeploysBeforeTheStackThatConsumesWhatItMakes() {
        var producer = Definition("producer", produces: [TheFunction]);
        var consumer = Definition("consumer", consumes: [TheFunction]);

        Assert.Equal(["producer", "consumer"], Sorted(consumer, producer));
    }

    /// <summary>
    /// The same pair the other way into the list. A sort that only answers correctly for one input
    /// order is not ordering anything — it is preserving what it was given.
    /// </summary>
    [Fact]
    public void TheProducerStillDeploysFirstWhenItWasAlreadyInFront() {
        var producer = Definition("producer", produces: [TheFunction]);
        var consumer = Definition("consumer", consumes: [TheFunction]);

        Assert.Equal(["producer", "consumer"], Sorted(producer, consumer));
    }

    [Fact]
    public void StacksThatShareNoResourcesKeepTheOrderTheyWereRegisteredIn() {
        var first = Definition("first", produces: [new CdkResourceRef<string>("a")]);
        var second = Definition("second", produces: [new CdkResourceRef<string>("b")]);

        Assert.Equal(["first", "second"], Sorted(first, second));
    }

    /// <summary>
    /// Producing a resource nobody consumes says nothing about order — the reference has to appear
    /// on both sides for the pair to be related at all.
    /// </summary>
    [Fact]
    public void ProducingAResourceTheOtherStackDoesNotConsumeOrdersNothing() {
        var producer = Definition("producer", produces: [TheFunction]);
        var unrelated = Definition("unrelated", consumes: [new CdkResourceRef<string>("queue")]);

        Assert.Equal(["unrelated", "producer"], Sorted(unrelated, producer));
    }

    [Fact]
    public void ALowerOrderDeploysFirst() {
        var late = Definition("late", order: 1000);
        var early = Definition("early", order: 0);

        Assert.Equal(["early", "late"], Sorted(late, early));
    }

    /// <summary>
    /// <c>Order</c> is consulted before <c>Produces</c>/<c>Consumes</c> and wins outright. That is
    /// what lets <see cref="Lambda.DeploymentGroupStack"/> place itself at the end on
    /// <c>Order = 1000</c> without naming a single resource — it depends on whatever was deployed,
    /// which no reference can express.
    /// </summary>
    [Fact]
    public void OrderWinsOverProducingWhatTheOtherStackConsumes() {
        var producer = Definition("producer", order: 1000, produces: [TheFunction]);
        var consumer = Definition("consumer", order: 0, consumes: [TheFunction]);

        Assert.Equal(["consumer", "producer"], Sorted(producer, consumer));
    }

    [Fact]
    public void StacksWithNoOrderAndNoResourcesInCommonAreLeftAsTheyAre() {
        Assert.Equal(
            ["a", "b", "c"],
            Sorted(Definition("a"), Definition("b"), Definition("c")));
    }

    /// <summary>
    /// The producer and the consumer are matched on the reference's value, not on the object: each
    /// stack definition writes its own <c>new CdkResourceRef&lt;T&gt;(...)</c>, or reads
    /// <see cref="KnownCdkResources"/>, and the two are never the same instance.
    /// </summary>
    [Fact]
    public void SeparatelyWrittenReferencesToTheSameResourceStillRelateTwoStacks() {
        var producer = Definition("producer", produces: [new CdkResourceRef<Function>("api")]);
        var consumer = Definition("consumer", consumes: [new CdkResourceRef<Function>("api")]);

        Assert.Equal(["producer", "consumer"], Sorted(consumer, producer));
    }

    private static string[] Sorted(params IStackDefinitionBase[] definitions) {
        var deployers = definitions.Select(d => (IStackDefinitionDeployer)new StubDeployer(d)).ToList();

        CdkDeployment.SortStackDefinitions(deployers);

        return deployers.Select(d => d.Name()).ToArray();
    }

    private static StubDefinition Definition(
        string name,
        int order = 0,
        ICdkResourceRef[]? produces = null,
        ICdkResourceRef[]? consumes = null) =>
        new() {
            Name = name,
            Order = order,
            Produces = produces ?? [],
            Consumes = consumes ?? [],
        };

    private sealed class StubDefinition : IStackDefinitionBase {
        public required string Name { get; init; }

        public int Order { get; init; }

        public IEnumerable<ICdkResourceRef> Produces { get; init; } = [];

        public IEnumerable<ICdkResourceRef> Consumes { get; init; } = [];
    }

    private sealed class StubDeployer(IStackDefinitionBase definition) : IStackDefinitionDeployer {
        public IStackDefinitionBase Definition => definition;

        public bool ShouldDeploy() => true;

        public string Name() => definition.Name;

        public string AccountType() => definition.AccountType;

        public object ConfigValue() => definition;

        public void Deploy() { }
    }
}
