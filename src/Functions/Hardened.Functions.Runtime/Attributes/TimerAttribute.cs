namespace Hardened.Functions.Runtime.Attributes;

/// <summary>
/// Runs the attributed handler on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// EventBridge Scheduler on AWS, a timer trigger on Azure, Cloud Scheduler on Google. The handler
/// names the schedule; the schedule expression itself is the deployment's, because a cron string
/// compiled into an assembly cannot be changed without a release.
/// </para>
/// <para>
/// Routes as <c>TIMER /nightly-rollup</c>. The payload a scheduled invocation carries is usually
/// empty, so a handler that takes no parameter is the ordinary case here rather than an oddity.
/// </para>
/// </remarks>
public class TimerAttribute : Attribute {
    public TimerAttribute(string name) {
        Name = name;
    }

    /// <summary>
    /// The schedule's name, which is what the delivered event carries back and what the deployment
    /// attaches an expression to.
    /// </summary>
    public string Name { get; }
}
