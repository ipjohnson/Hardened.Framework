using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CodeDeploy;
using Amazon.CDK.AWS.Lambda;
using Cdklabs.CdkMonitoringConstructs;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Lambda;

public class DeploymentGroupStack : IStackDefinition {

    public int Order => 1000;

    public bool ShouldDeploy(IStackDeploymentContext context) {
        return context.Resources.Any(x => x.Item1 is Alias);
    }

    public string NameValue(IStackDeploymentContext context) {
        var function = 
            context.Resources
                .Where(x => x.Item1 is Alias)
                .Select(x => x.Item1)
                .Cast<Alias>();

        return function.Single().FunctionName + "-deployment-group";
    }

    public void Deploy(IStackDeploymentContext context) {
        
        var aliases = 
            context.Resources.Where(x => x.Item1 is Alias).
                Select(x => new Tuple<Alias,string>((Alias)x.Item1, x.Item2)).ToList();

        var monitor = new MonitoringFacade(context.Stack, "", new MonitoringFacadeProps { });

        monitor.MonitorLambdaFunction(new LambdaFunctionMonitoringProps {
        });
        
        var config = context.Stage == StageType.Prod ? 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_10MINUTES : 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_3MINUTES;
        
        foreach (var alias in aliases) {
            var name = alias.Item2;
            var application = new LambdaApplication(context.Stack, name + "-app-dg", new LambdaApplicationProps {
                ApplicationName = name + "-app-dg",
            });
            
            var d = new LambdaDeploymentGroup(context.Stack, name + "-dg", new LambdaDeploymentGroupProps {
                Application = application,
                Alias = alias.Item1,
                Alarms = [
                    new Alarm(context.Stack, name + "-Alarm",new AlarmProps {
                        Metric = alias.Item1.MetricErrors(new MetricOptions { 
                            Period = Duration.Minutes(1)
                        }),
                        AlarmName = name + "-Alarm",
                    })
                ],
                DeploymentConfig = config,
            });
        }
    }
}