using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureQueue.Settlement.SUT;

/// <summary>
/// The same handlers, deployed against a function that settles each message itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>The module is written out here, and only here.</b> Everywhere else the trigger binds it and
/// the application names no cloud; this one has to say <c>[ServiceBusModule(...)]</c> because
/// per-message settlement is not a property of the code at all - it is whether the function was
/// deployed with the host's automatic completion turned off and the settlement channel bound, and
/// the code has no way to know. Applying the module explicitly is how a deployment says so, and
/// the generator reads it into the function's binding as <c>autoCompleteMessages: false</c>.
/// </para>
/// <para>
/// Getting it wrong in this direction loses messages: a function that completes and abandons by
/// hand while the host also completes on its behalf has every failed message completed for it.
/// That is why it is off by default and why saying so takes a deliberate line -
/// <c>PartialFailureApp</c> on SQS is the same line for the same reason.
/// </para>
/// </remarks>
[HardenedModule]
[ServiceBusModule(ReportsItemFailures = true)]
public partial class SettlementTestApp {
}
