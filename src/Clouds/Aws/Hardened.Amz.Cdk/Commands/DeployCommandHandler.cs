using Amazon.CDK;
using Hardened.Commands;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Environment = Amazon.CDK.Environment;

namespace Hardened.Amz.Cdk.Commands;

[Expose]
public class DeployCommandHandler : ICommandHandler<DeployCommand> {
    private CdkConfigurationRegistry _registry;
    private IServiceProvider _serviceProvider;
    private IHardenedEnvironment _hardenedEnvironment;
    private IDeploymentAccountProvider _deploymentAccountProvider;
    private readonly StackContextAccessor _stackContextAccessor;

    public DeployCommandHandler(
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

    public async Task<int> Handle(DeployCommand value) {
        var cdkApp = _hardenedEnvironment.CustomData<App>("cdkApp");
        
        if (cdkApp == null) {
            throw new Exception("Could not find cdkApp in environment");
        }
        
        var configProvider = _serviceProvider.GetService<ICdkConfigurationProvider>();

        if (configProvider == null) {
            throw new ApplicationException("No ICdkConfigurationProvider exposed, please implement.");
        }

        var (stageType, regionType) = GetRegionAndStageType();
        
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
                Console.WriteLine("Deploying Stack Definition: " + deployer.Definition.Name);
                var account = _deploymentAccountProvider.GetDeploymentAccount(
                    deployer.ConfigValue(), deployer.AccountType());

                var stack = new Stack(cdkApp, deployer.Name() + "Id", new StackProps {
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
        
        return 0;
    }

    private IEnumerable<IStackDefinitionDeployer> GetDefaultStackDeployers(IServiceProvider serviceProvider, IStackDeploymentContext context) {
        var stackDefinitions = serviceProvider.GetServices<IStackDefinition>();

        foreach (var definition in stackDefinitions) {
            yield return new CdkConfigurationRegistry.StackDefinitionDeployer(context, definition);
        }
    }


    private void SortStackDefinitions(List<IStackDefinitionDeployer> deployers) {
        deployers.Sort((xDeployer, yDeployer) => {
            var x = xDeployer.Definition;
            var y = yDeployer.Definition;
            
            if (x.Order != y.Order) {
                return x.Order.CompareTo(y.Order);
            }

            foreach (var produced in x.Produces) {
                if (y.Consumes.Contains(produced)) {
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