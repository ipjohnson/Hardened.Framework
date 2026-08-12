using Amazon.CDK;
using Amazon.CDK.CXAPI;
using Hardened.Amz.Cdk.Commands;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hardened.Amz.Cdk.Tests;

/// <summary>
/// The deploy command is the whole CDK package in one method: it finds the application's
/// configuration, turns registered stack definitions into deployers, orders them, gives each one a
/// CloudFormation stack in the right account, and synthesises.
///
/// <para>
/// Driven through a real <see cref="App"/> and a real synth rather than mocks, because the
/// interesting outcomes — which stacks exist, whose account they deploy into, what each definition
/// was handed — are only observable in what CDK produced.
/// </para>
/// </summary>
public class DeployCommandHandlerTests : IDisposable {

    private readonly List<string> _outputDirectories = [];

    [Fact]
    public async Task EveryDeployedStackDefinitionBecomesACloudFormationStack() {
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("orders", deployment.DeployLog));
        deployment.Add(new RecordingStackDefinition("billing", deployment.DeployLog));

        var stacks = await deployment.Deploy();

        Assert.Equal(["billing-stack", "orders-stack"], stacks.Select(s => s.StackName).Order());
    }

    /// <summary>
    /// The name a stack definition reports is not decoration — it becomes the CloudFormation stack
    /// name, so changing it deletes one stack and creates another.
    /// </summary>
    [Fact]
    public async Task AStackIsNamedForItsDefinitionWithStackAppended() {
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("orders", deployment.DeployLog));

        var stack = Assert.Single(await deployment.Deploy());

        Assert.Equal("orders-stack", stack.StackName);
    }

    /// <summary>
    /// The account is read off the configuration by the property named in <c>AccountType</c>, so a
    /// deployment reaching two accounts describes both on one configuration object.
    /// </summary>
    [Fact]
    public async Task AStackDeploysIntoTheAccountItsConfigurationNames() {
        var deployment = Deployment(new TestStageConfiguration(KnownRegion.UsWest2, StageType.Dev) {
            ServiceAccount = "444455556666",
        });
        var orders = new RecordingStackDefinition("orders", deployment.DeployLog);
        deployment.Add(orders);

        await deployment.Deploy();

        Assert.Equal("444455556666", orders.DeployedInto?.Account);
    }

    /// <summary>
    /// The region is the one the deployment was invoked with, not the one on the configuration. The
    /// configuration describes a stage in a region; the command deploys the region it was asked for.
    /// </summary>
    [Fact]
    public async Task TheRegionComesFromTheContextTheAppWasInvokedWith() {
        var deployment = Deployment(region: "eu-west-1");
        var orders = new RecordingStackDefinition("orders", deployment.DeployLog);
        deployment.Add(orders);

        await deployment.Deploy();

        Assert.Equal("eu-west-1", orders.DeployedInto?.Region);
    }

    [Fact]
    public async Task TheStageAndRegionAreHandedToTheApplicationsConfigurationProvider() {
        var deployment = Deployment(region: "us-east-2", stage: "gamma");
        deployment.Add(new RecordingStackDefinition("orders", deployment.DeployLog));

        await deployment.Deploy();

        Assert.Equal("gamma", deployment.ConfigurationProvider.Stage);
        Assert.Equal("us-east-2", deployment.ConfigurationProvider.Region);
    }

    /// <summary>
    /// The opt-out is per deployment, not per registration: a definition is registered once and
    /// decides against a given stage or region by looking at the context it is handed.
    /// </summary>
    [Fact]
    public async Task AStackThatOptsOutIsNeitherCreatedNorDeployed() {
        var deployment = Deployment();
        var skipped = new RecordingStackDefinition("skipped", deployment.DeployLog) { Deployable = false };
        deployment.Add(skipped);
        deployment.Add(new RecordingStackDefinition("deployed", deployment.DeployLog));

        var stacks = await deployment.Deploy();

        Assert.Equal("deployed-stack", Assert.Single(stacks).StackName);
        Assert.Equal(["deployed"], deployment.DeployLog);
        Assert.Null(skipped.DeployedInto);
    }

    [Fact]
    public async Task ADeploymentWhereEveryStackOptsOutSynthesisesNothing() {
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("a", deployment.DeployLog) { Deployable = false });
        deployment.Add(new RecordingStackDefinition("b", deployment.DeployLog) { Deployable = false });

        Assert.Empty(await deployment.Deploy());
    }

    /// <summary>
    /// End to end, through the container and a real synth, the ordering
    /// <see cref="StackOrderingTests"/> covers pairwise: the stack producing the function deploys
    /// before the stack consuming it, whichever order they were registered in.
    /// </summary>
    [Fact]
    public async Task AProducingStackIsDeployedBeforeTheStackConsumingItsResource() {
        var resource = new CdkResourceRef<string>("orders-table");
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("consumer", deployment.DeployLog) {
            Consumes = [resource],
        });
        deployment.Add(new RecordingStackDefinition("producer", deployment.DeployLog) {
            Produces = [resource],
        });

        await deployment.Deploy();

        Assert.Equal(["producer", "consumer"], deployment.DeployLog);
    }

    /// <summary>
    /// The consuming stack reads the producer's resource out of the shared context, which only
    /// works because the producer ran first. This is what the reversed comparison broke.
    /// </summary>
    [Fact]
    public async Task AConsumingStackCanReachWhatTheProducerDeployed() {
        var resource = new CdkResourceRef<string>("orders-table");
        var deployment = Deployment();
        var consumer = new ReadingStackDefinition("consumer", resource) { Consumes = [resource] };
        deployment.Add(consumer);
        deployment.Add(new RecordingStackDefinition("producer", deployment.DeployLog) {
            Produces = [resource],
        });

        await deployment.Deploy();

        Assert.Equal("producer", consumer.WhatItFound);
    }

    /// <summary>
    /// Each definition is deployed into its own stack, and reads it off the shared context — so the
    /// context has to carry the right one at the moment each definition runs, not the last one
    /// created.
    /// </summary>
    [Fact]
    public async Task EachDefinitionIsHandedTheStackCreatedForIt() {
        var deployment = Deployment();
        var first = new RecordingStackDefinition("first", deployment.DeployLog);
        var second = new RecordingStackDefinition("second", deployment.DeployLog);
        deployment.Add(first);
        deployment.Add(second);

        await deployment.Deploy();

        Assert.Equal("first-stack", first.DeployedInto?.StackName);
        Assert.Equal("second-stack", second.DeployedInto?.StackName);
    }

    /// <summary>
    /// The accessor is how a construct helper such as <c>LambdaCdkUtil</c> reaches the deployment
    /// context without every definition threading it through.
    /// </summary>
    [Fact]
    public async Task TheDeploymentContextIsPublishedOnTheAccessorForConstructHelpers() {
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("orders", deployment.DeployLog));

        await deployment.Deploy();

        Assert.Equal("orders", deployment.StackContextAccessor.Context.DeploymentName);
    }

    /// <summary>
    /// A stack definition registered without a configuration type deploys alongside the typed ones.
    /// It is the shape for a stack that needs nothing from the stage configuration.
    /// </summary>
    [Fact]
    public async Task AStackDefinitionWithNoConfigurationTypeDeploysAlongsideTheTypedOnes() {
        var deployment = Deployment();
        deployment.Add(new RecordingStackDefinition("typed", deployment.DeployLog));
        deployment.AddUntyped(new UntypedStackDefinition("untyped", deployment.DeployLog));

        var stacks = await deployment.Deploy();

        Assert.Equal(["typed-stack", "untyped-stack"], stacks.Select(s => s.StackName).Order());
    }

    /// <summary>
    /// Implementing <see cref="ICdkConfigurationProvider"/> is the one thing a consumer of this
    /// package must do, so not having done it is the most likely way a first deployment fails. The
    /// message names the interface.
    /// </summary>
    [Fact]
    public async Task ADeploymentWithNoConfigurationProviderSaysWhichInterfaceToImplement() {
        var deployment = Deployment(registerConfigurationProvider: false);

        var error = await Assert.ThrowsAsync<ApplicationException>(deployment.Deploy);

        Assert.Contains(nameof(ICdkConfigurationProvider), error.Message);
        Assert.Contains("please implement", error.Message);
    }

    /// <summary>
    /// A provider that runs and registers nothing leaves no configuration to deploy against, which
    /// is a different mistake from not having a provider at all.
    /// </summary>
    [Fact]
    public async Task AConfigurationProviderThatRegistersNothingIsReportedSeparately() {
        var deployment = Deployment();
        deployment.ReplaceConfigurationProviderWithAnEmptyOne();

        var error = await Assert.ThrowsAsync<ApplicationException>(deployment.Deploy);

        Assert.Contains("No Stack Definitions", error.Message);
    }

    [Fact]
    public async Task DeployingWithoutACdkAppInTheEnvironmentIsReported() {
        var deployment = Deployment(provideCdkApp: false);

        var error = await Assert.ThrowsAnyAsync<Exception>(deployment.Deploy);

        Assert.Contains("cdkApp", error.Message);
    }

    private DeploymentUnderTest Deployment(
        TestStageConfiguration? configuration = null,
        string region = "us-east-1",
        string stage = "dev",
        bool registerConfigurationProvider = true,
        bool provideCdkApp = true) {

        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "hardened-cdk-tests", Guid.NewGuid().ToString("N"));
        _outputDirectories.Add(outputDirectory);

        return new DeploymentUnderTest(
            configuration ?? new TestStageConfiguration(KnownRegion.UsEast1, StageType.Dev),
            outputDirectory,
            region,
            stage,
            registerConfigurationProvider,
            provideCdkApp);
    }

    public void Dispose() {
        foreach (var directory in _outputDirectories.Where(Directory.Exists)) {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A deployment assembled the way a consuming application assembles one: a CDK app carrying the
    /// stage and region in its context, a container holding the stack definitions, and the handler
    /// wired to both.
    /// </summary>
    private sealed class DeploymentUnderTest {
        private readonly ServiceCollection _services = [];
        private readonly App _app;
        private readonly bool _provideCdkApp;

        public DeploymentUnderTest(
            TestStageConfiguration configuration,
            string outputDirectory,
            string region,
            string stage,
            bool registerConfigurationProvider,
            bool provideCdkApp) {

            _provideCdkApp = provideCdkApp;
            _app = new App(new AppProps {
                Outdir = outputDirectory,
                Context = new Dictionary<string, object> {
                    ["stage"] = stage,
                    ["region"] = region,
                },
            });

            ConfigurationProvider = new TestConfigurationProvider(configuration);

            _services.AddTransient<IStackDefinitionProvider, StackDefinitionProvider>();

            if (registerConfigurationProvider) {
                _services.AddSingleton<ICdkConfigurationProvider>(ConfigurationProvider);
            }
        }

        public List<string> DeployLog { get; } = [];

        public TestConfigurationProvider ConfigurationProvider { get; }

        public StackContextAccessor StackContextAccessor { get; } = new();

        public void Add(IStackDefinition<TestStageConfiguration> definition) =>
            _services.AddSingleton(definition);

        public void AddUntyped(IStackDefinition definition) => _services.AddSingleton(definition);

        public void ReplaceConfigurationProviderWithAnEmptyOne() {
            _services.RemoveAll<ICdkConfigurationProvider>();
            _services.AddSingleton<ICdkConfigurationProvider>(new EmptyConfigurationProvider());
        }

        public async Task<CloudFormationStackArtifact[]> Deploy() {
            var serviceProvider = _services.BuildServiceProvider();

            var handler = new DeployCommandHandler(
                new CdkConfigurationRegistry(serviceProvider),
                serviceProvider,
                new EnvironmentImpl(customData: _provideCdkApp
                    ? new Dictionary<string, object> { ["cdkApp"] = _app }
                    : new Dictionary<string, object>()),
                new DeploymentAccountProvider(),
                StackContextAccessor);

            var exitCode = await handler.Handle(new DeployCommand());

            Assert.Equal(0, exitCode);

            return _app.Synth().Stacks;
        }
    }

    /// <summary>Reads the resource a producing stack deployed, which only works if it ran first.</summary>
    private sealed class ReadingStackDefinition(string name, CdkResourceRef<string> resource)
        : IStackDefinition<TestStageConfiguration> {

        public string Name => name;

        public IEnumerable<ICdkResourceRef> Consumes { get; init; } = [];

        public string? WhatItFound { get; private set; }

        public void Deploy(IStackDeploymentContext<TestStageConfiguration> context) =>
            WhatItFound = context.Get(resource);
    }
}
