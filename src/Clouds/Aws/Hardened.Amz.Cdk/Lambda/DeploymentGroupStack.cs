using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CodeDeploy;
using Amazon.CDK.AWS.Lambda;
using Cdklabs.CdkMonitoringConstructs;
using Hardened.Amz.Shared.Lambda.Runtime.Configuration;

namespace Hardened.Amz.Cdk.Lambda;

//[TransientService]
public class DeploymentGroupStack : IStackDefinition {

    public int Order => 1000;

    public bool ShouldDeploy(IStackDeploymentContext context) {
        return context.Resources.Any(x => x.Item1 is Alias);
    }

    public string NameValue(IStackDeploymentContext context) {

        return context.DeploymentName + "-deployment-group";
    }

    public void Deploy(IStackDeploymentContext context) {
        
        // Filter and cast in one step. Where(x => x.Item1 is Alias) followed by a separate
        // (Alias)x.Item1 casts a value the compiler still sees as object?, which it cannot connect
        // back to the test in the earlier clause.
        var aliases = new List<Tuple<Alias, string>>();

        foreach (var resource in context.Resources) {
            if (resource.Item1 is Alias alias) {
                aliases.Add(new Tuple<Alias, string>(alias, resource.Item2));
            }
        }

        var config = context.Stage.IsProduction ? 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_10MINUTES : 
            LambdaDeploymentConfig.LINEAR_10PERCENT_EVERY_3MINUTES;

        var alarms = new List<IAlarmRule>();
        
        
        foreach (var alias in aliases) {
            context.Stack.AddStackDependency(alias.Item1.Stack);
            
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

            var lambdaDeploymentGroup = new LambdaDeploymentGroup(context.Stack, name + "-dg", new LambdaDeploymentGroupProps {
                Application = application,
                Alias = alias.Item1,
                Alarms = [rollbackAlarm],
                DeploymentConfig = config,
            });
        }
    }
}