using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureEvents.SUT;

/// <summary>
/// One function app serving a queue, a topic, a schedule and an event source.
/// </summary>
/// <remarks>
/// <para>
/// Four triggers across the handlers pull in three modules - Service Bus serves both the queue
/// and the topic - and nothing here names an adapter. What it does name is a subscription:
/// <c>[Topic("order-events")]</c> names the topic, and on Service Bus a function reads a topic
/// through a subscription the neutral trigger has no slot for. That is a deployment fact, so it is
/// said once on the application and the generator writes it into every topic function's binding -
/// and refuses to build without it, as HRDAZ003. <c>EventsTestApp</c> on Lambda is this file
/// without the line, because an SNS subscription is wired outside the code.
/// </para>
/// </remarks>
[HardenedModule]
[ServiceBusModule(Subscription = "events-function")]
public partial class AzureEventsTestApp {
}
