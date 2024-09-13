using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CodeDeploy;
using Amazon.CDK.AWS.Lambda;
using Cdklabs.CdkMonitoringConstructs;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Lambda;

[Expose]
public class DeploymentGroupStack : IStackDefinition {

    public int Order => 1000;

    public bool ShouldDeploy(IStackDeploymentContext context) {
        return context.Resources.Any(x => x.Item1 is Alias);
    }

    public string NameValue(IStackDeploymentContext context) {

        return context.DeploymentName + "-deployment-group";
    }

    public void Deploy(IStackDeploymentContext context) {
        
        var aliases = 
            context.Resources.Where(x => x.Item1 is Alias).
                Select(x => new Tuple<Alias,string>((Alias)x.Item1, x.Item2)).ToList();

        var monitor = new MonitoringFacade(context.Stack, "", new MonitoringFacadeProps { });

        monitor.MonitorLambdaFunction(new LambdaFunctionMonitoringProps {
        });
        
        var config = context.Stage.IsProduction ? 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_10MINUTES : 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_3MINUTES;

        var alarms = new List<IAlarmRule>();
        
        foreach (var alias in aliases) {
            var name = alias.Item2;
            var failureAlarm = new Alarm(context.Stack, name + "-failure-alarm", new AlarmProps {
                AlarmName = name + "-failure",
                Metric = alias.Item1.MetricErrors(new MetricOptions {
                    Period = Duration.Minutes(1),
                    Statistic = Stats.SUM,
                }),
                EvaluationPeriods = 5,
                Threshold = 3
            });
            
            alarms.Add(failureAlarm);
        }
        
        var rollBackAlarm = context.DeploymentName + "-rollback-alarm";
        
        var rollbackComposite = new CompositeAlarm(context.Stack, rollBackAlarm + "-comp", new CompositeAlarmProps {
            CompositeAlarmName = rollBackAlarm,
            AlarmRule = AlarmRule.AnyOf(alarms.ToArray())
        });
        
        var rollbackAlarm = new CompositeAlarm(context.Stack, rollBackAlarm, new CompositeAlarmProps {
            CompositeAlarmName = rollBackAlarm,
            AlarmRule = AlarmRule.AnyOf(rollbackComposite)
        });
        
        foreach (var alias in aliases) {
            var name = alias.Item2;
            var application = new LambdaApplication(context.Stack, name + "-app-dg", new LambdaApplicationProps {
                ApplicationName = name + "-app-dg",
            });

            var d = new LambdaDeploymentGroup(context.Stack, name + "-dg", new LambdaDeploymentGroupProps {
                Application = application,
                Alias = alias.Item1,
                Alarms = [rollbackAlarm],
                DeploymentConfig = config,
            });
        }
    }
}