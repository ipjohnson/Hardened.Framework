using Amazon.CDK;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Environment = Amazon.CDK.Environment;

namespace Hardened.Amz.Cdk.Commands;

/// <summary>
/// What a CDK deployment application does when it runs.
/// </summary>
/// <remarks>
/// <para>
/// This was an <c>ICommandHandler&lt;DeployCommand&gt;</c> from <c>Hardened.Commands</c>, where
/// <c>DeployCommand</c> was an empty class carrying <c>[Command("")]</c> and the handler never read
/// the value it was passed. A deployment takes no arguments — stage and region come off the CDK
/// app's own context — so the whole command line layer was parsing nothing on the way to a single
/// entry point.
/// </para>
/// <para>
/// <see cref="IApplicationDelegateProvider"/> is that entry point without the layer: it is the seam
/// the generated <c>Run()</c> already asks for, and command line parsing was only ever one
/// implementation of it.
/// </para>
/// </remarks>
[TransientService(As = typeof(IApplicationDelegateProvider))]
public class CdkDeployment : IApplicationDelegateProvider {
    private readonly CdkConfigurationRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHardenedEnvironment _hardenedEnvironment;
    private readonly IDeploymentAccountProvider _deploymentAccountProvider;
    private readonly StackContextAccessor _stackContextAccessor;

    public CdkDeployment(
        CdkConfigurationRegistry registry,
        IServiceProvider serviceProvider,
        IHardenedEnvironment hardenedEnvironment,
        IDeploymentAccountProvider deploymentAccountProvider, 
        StackContextAccessor stackContextAccessor) {
        _registry = registry;
        _serviceProvider = serviceProvider;
        _hardenedEnvironment = hardenedEnvironment;
        _deploymentAccountProvider = deploymentAccountProvider;
        _stackContextAccessor = stackContextAccessor;
    }

    /// <summary>Startup services run first: they are what register the configuration providers
    /// <see cref="Deploy"/> resolves.</summary>
    public Task<ApplicationDelegate> ProvideDelegate(
        IHardenedEnvironment environment, IServiceProvider serviceProvider) =>
        Task.FromResult(new ApplicationDelegate(Deploy, ShouldStartApp: true));

    public Task<int> Deploy() {
        var cdkApp = _hardenedEnvironment.CustomData<App>("cdkApp");
        
        if (cdkApp == null) {
            throw new Exception("Could not find cdkApp in environment");
        }
        
        var configProvider = _serviceProvider.GetService<ICdkConfigurationProvider>();

        if (configProvider == null) {
            throw new ApplicationException("No ICdkConfigurationProvider exposed, please implement.");
        }

        var (stageType, regionType) = GetRegionAndStageType();
        
        Console.WriteLine($"Deploying ${stageType} to {regionType}");
        
        configProvider.ProvideConfiguration(stageType, regionType, _registry);

        if (_registry.TypedStackDefinition == null) {
            throw new ApplicationException("No Stack Definitions exposed, please implement.");
        }
        
        var deployers = 
            _registry.TypedStackDefinition.GetTypedDeployers(_serviceProvider).ToList();

        deployers.AddRange(GetDefaultStackDeployers(_serviceProvider, _registry.TypedStackDefinition.Context));
        
        SortStackDefinitions(deployers);

        _stackContextAccessor.Context = _registry.TypedStackDefinition.Context;
        
        foreach (var deployer in deployers) {
            if (deployer.ShouldDeploy()) {
                var name = deployer.Name();
                Console.WriteLine("Deploying Stack Definition: " + name);
                var account = _deploymentAccountProvider.GetDeploymentAccount(
                    deployer.ConfigValue(), deployer.AccountType());

                var stack = new Stack(cdkApp, name + "-stack", new StackProps {
                    Env = new Environment { Account = account, Region = regionType }
                });
                
                _registry.TypedStackDefinition.Context.Stack = stack;
                
                deployer.Deploy();
            }
            else {
                Console.WriteLine("Skipping Stack Definition: " + deployer.Definition.Name);
            }
        }

        cdkApp.Synth();
        
        return Task.FromResult(0);
    }

    private IEnumerable<IStackDefinitionDeployer> GetDefaultStackDeployers(IServiceProvider serviceProvider, IStackDeploymentContext context) {
        var stackDefinitions = serviceProvider.GetServices<IStackDefinition>();

        foreach (var definition in stackDefinitions) {
            yield return new CdkConfigurationRegistry.StackDefinitionDeployer(context, definition);
        }
    }


    /// <summary>
    /// Orders the stacks so that a stack producing a resource deploys before the stacks consuming
    /// it. <c>Order</c> is consulted first and wins outright — it is how a stack such as
    /// <c>DeploymentGroupStack</c>, which depends on whatever happens to have been deployed rather
    /// than on a named resource, places itself at the end.
    ///
    /// <para>
    /// Fixed 2026-08-11: the two comparisons were the wrong way round, so a producer sorted
    /// <em>after</em> every stack consuming what it produced. The consumer then ran first and its
    /// <c>context.Get</c> threw, because nothing had called <c>context.Set</c> yet. Nothing caught
    /// it because the assembly had no tests at all.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Pairwise comparison, not a graph walk: it orders a producer against its direct consumer and
    /// says nothing about a chain, since two stacks connected only through a third compare equal.
    /// <c>Order</c> is the way to express a longer chain.
    /// </remarks>
    internal static void SortStackDefinitions(List<IStackDefinitionDeployer> deployers) {
        deployers.Sort((xDeployer, yDeployer) => {
            var x = xDeployer.Definition;
            var y = yDeployer.Definition;

            if (x.Order != y.Order) {
                return x.Order.CompareTo(y.Order);
            }

            foreach (var produced in x.Produces) {
                if (y.Consumes.Contains(produced)) {
                    // x makes something y needs, so x goes first.
                    return -1;
                }
            }

            foreach (var produced in y.Produces) {
                if (x.Consumes.Contains(produced)) {
                    return 1;
                }
            }

            return 0;
        });
    }
    
    private (string stageType, string regionType) GetRegionAndStageType() {
        var cdkApp = _hardenedEnvironment.CustomData<App>("cdkApp");

        if (cdkApp == null) {
            throw new ApplicationException("Could not find cdk app");
        }
        
        var stage = (string)cdkApp.Node.TryGetContext("stage");
        var region = (string)cdkApp.Node.TryGetContext("region");
        
        return (stage, region);
    }
}