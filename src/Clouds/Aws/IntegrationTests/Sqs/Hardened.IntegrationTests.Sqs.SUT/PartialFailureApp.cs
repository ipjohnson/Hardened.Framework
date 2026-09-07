using Hardened.Aws.Lambda.Runtime.Modules;
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.Sqs.SUT;

/// <summary>
/// The same handlers, deployed against a mapping that reports individual message failures.
/// </summary>
/// <remarks>
/// <para>
/// <b>The module is written out here, and only here.</b> Everywhere else the trigger binds it and
/// the application names no cloud; this one has to say <c>[SqsModule(...)]</c> because
/// <c>ReportBatchItemFailures</c> is not a property of the code at all - it is whether the event
/// source mapping was deployed with it turned on, and the code has no way to know. Applying the
/// module explicitly is how a deployment says so.
/// </para>
/// <para>
/// Getting it wrong in this direction loses messages: a report sent to a mapping that did not ask
/// for one is discarded and the whole batch is marked successful. That is why it is off by default
/// and why saying so takes a deliberate line.
/// </para>
/// </remarks>
[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class PartialFailureApp {
}
