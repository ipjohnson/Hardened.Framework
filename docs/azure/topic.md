# Topics

`[Topic("orders")]` on a method makes it the handler for messages published to the Azure Service
Bus topic named `orders`. The handler reads one subscription of the topic, which
`[ServiceBusModule(Subscription = "Orders")]` names on the application.

The `hardened-function` template writes this handler in `src/Orders/OrderHandler.cs`:

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Topic("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

It writes the application class in `src/Orders/Application.cs`:

```csharp
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[ServiceBusModule(Subscription = "Orders")]
public partial class Application;
```

For one message published to the topic `orders`, `func start` prints these lines, here without
their timestamps:

```text
Functions:

	Topic_orders: serviceBusTrigger

Executing 'Functions.Topic_orders' (Reason='(null)', Id=d7bc9ab3-3d2b-44d0-89db-3592045c940e)
TOPIC /orders started
TOPIC /orders  finished status code '(null)'  duration 00:00:00.0198150
Executed 'Functions.Topic_orders' (Succeeded, Id=d7bc9ab3-3d2b-44d0-89db-3592045c940e, Duration=132ms)
```

`Topic_orders` is the Azure function that the build writes for the handler. `TOPIC /orders` is the
route that the handler runs under. The handler runs once for each message of a batch, as a queue
handler does. The Azure [Overview](/azure/) covers running the app locally with `func start`.

In the template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a
`[SingletonService]` that keeps the orders it is given. The template names the subscription after
the project. `-n Orders` gives `Subscription = "Orders"`.

Service Bus gives each subscription of a topic its own copy of the messages published to the topic.
A subscription gets every message unless its rules filter some out
([Azure Service Bus Queues, Topics, and Subscriptions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-queues-topics-subscriptions)).

A reference to `Hardened.Azure.Functions.ServiceBus` makes `[Topic]` mean a Service Bus topic.
[Triggers](/guide/triggers) covers `[Topic]` and the other trigger attributes.

## Packages and the template

This command writes the app above and a test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger topic
```

A topic function references `Hardened.Azure.Functions.ServiceBus` beside
`Hardened.Azure.Functions.Runtime` and `Microsoft.Azure.Functions.Worker.Sdk`. These are the two
Hardened package references:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.ServiceBus" Version="0.0.0-HARDENED-VERSION" />
```

The same Service Bus package serves `[Queue]`, which [Queues](/azure/queue) covers. The other
packages, `Program.cs`, `host.json` and the project settings are the same for every Azure function
app. The Azure [Overview](/azure/) covers them.

## Functions and routes

The build writes one Azure function for each `[Topic]` handler. The function is bound to the topic
that `[Topic]` names and to the subscription that the module names. The handler runs under a route
that comes from the function. The function's name follows the rule that the Azure
[Overview](/azure/) gives:

| Declaration | Function | Route |
|---|---|---|
| `[Topic("orders")]` | `Topic_orders` | `TOPIC /orders` |
| `[Topic("order-events")]` | `Topic_order_events` | `TOPIC /order-events` |
| `[Queue("orders")]` | `Queue_orders` | `QUEUE /orders` |

`[Queue("orders")]` and `[Topic("orders")]` in one application are two functions.

The host invokes `Topic_orders` only with messages from the subscription `Orders` of the topic
`orders`. Every `[Topic]` handler of the application reads through the one subscription. With
`[Topic("payments")]` beside `[Topic("orders")]`, the function `Topic_payments` reads the
subscription `Orders` of the topic `payments`.

The build writes the binding into `functions.metadata` beside the assembly. The host reads the
binding from that file. For the template's app, `src/Orders/bin/Debug/net8.0/functions.metadata`
holds this entry:

```json
[
  {
    "name": "Topic_orders",
    "scriptFile": "Orders.dll",
    "entryPoint": "Orders.Generated.ApplicationAzureFunctions.Topic_orders",
    "language": "dotnet-isolated",
    "properties": {
      "IsCodeless": false
    },
    "bindings": [
      {
        "name": "messages",
        "direction": "In",
        "type": "serviceBusTrigger",
        "topicName": "orders",
        "subscriptionName": "Orders",
        "cardinality": "Many",
        "properties": {
          "supportsDeferredBinding": "True"
        }
      }
    ]
  }
]
```

A subscription's name holds 1 to 50 letters, digits, periods, hyphens and underscores. It starts and
ends with a letter or a digit. A topic's name may also hold slashes
([Naming rules and restrictions for Azure resources](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules)).

Service Bus matches the names of the topic and the subscription without regard to case. With
`Subscription = "ORDERS"` on the module, `[Topic("PAYMENTS")]` receives the messages of the
subscription `Orders` of the topic `payments`. The handler runs under the route `TOPIC /PAYMENTS`.

The topic and the subscription have to exist. Until they do, the handler never runs. The host logs
that the messaging entity could not be found, about twice a second.

The messages of a session-enabled subscription never reach the handler, as with a session-enabled
queue. [Queues](/azure/queue) covers the session-enabled case.

## Body and headers

A topic's message reaches the handler as a queue's message does. The body binds as JSON. Each
application property becomes a header. `x-azure-servicebus-message-id`,
`x-azure-servicebus-delivery-count` and `Content-Type` carry the message id, the delivery count and
the content type. [Queues](/azure/queue) lists the headers. The names of the topic and the
subscription are not headers.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in namespace
`Hardened.Requests.Abstract.Execution`. This handler reads two headers and logs them:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Topic("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-azure-servicebus-message-id", out var messageId);
        request.Headers.TryGetValue("tenant", out var tenant);

        logger.LogInformation(
            "Message {MessageId} for tenant {Tenant}",
            messageId.ToString(),
            tenant.ToString()
        );

        log.Record(order);
    }
}
```

A header can be missing. The handler reads each header with `TryGetValue`.

A message with these fields on the topic `orders` runs the handler:

| Message field | Value |
|---|---|
| Body | `{"id":"A-1","quantity":2}` |
| `ContentType` | `application/json` |
| `MessageId` | `order-A-1` |
| Application property `tenant` | `acme` |

The handler logs `Message order-A-1 for tenant acme`. The host logs the invocation as `Succeeded`.

## Failures and redelivery

A failed message is handled as it is on a queue. By default, a failed message fails the invocation.
The host then abandons the whole batch. With `ReportsItemFailures` on, every message runs. Only the
failed message is abandoned. [Queues](/azure/queue) covers both cases and the lock that limits how
long a batch can run.

The lock duration and the `MaxDeliveryCount` that apply are the subscription's
([az servicebus topic subscription](https://learn.microsoft.com/en-us/cli/azure/servicebus/topic/subscription)).
A message whose delivery count passes the subscription's `MaxDeliveryCount` moves to the
subscription's dead-letter queue. `MaxDeliveryCount` is 10 by default. The dead-letter queue of the
subscription `Orders` of the topic `orders` is `orders/Subscriptions/Orders/$deadletterqueue`
([Service Bus Dead-Letter Queues](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues)).

A message that the function abandons comes back only to its own subscription. The other
subscriptions keep their own copies.

::: warning
Two function apps that set the same `Subscription` on one topic share the subscription's messages.
Each message reaches only one of them. Neither app logs anything about the other. Give each function
app its own subscription.
:::

## Module settings

`ServiceBusModule` is in namespace `Hardened.Azure.Functions.ServiceBus`. A `[Topic]` handler needs
`Subscription` on the module. Without it, the build fails with `HRDAZ003`:

```text
error HRDAZ003: The [Topic] handlers need Subscription on [ServiceBusModule], and this application does not set it. Write [ServiceBusModule(Subscription = "...")] on the application, beside [HardenedModule].
```

`Subscription` has to be a literal. A constant fails the build with `HRDAZ004`. For
`Subscription = Names.Subscription`, where `Names.Subscription` is a `const string` in namespace
`Orders`, the build reports:

```text
error HRDAZ004: Subscription on [ServiceBusModule] is written as global::Orders.Names.Subscription, and the function metadata the host indexes needs its value at build. Write it as a string literal, an integer literal, or true or false.
```

[Diagnostics](/reference/diagnostics) lists every code.

One module sets these properties for the application's queue and topic functions:

| Property | `[Topic]` functions | `[Queue]` functions |
|---|---|---|
| `Subscription` | Required | Ignored |
| `Connection` | Applies | Applies |
| `ReportsItemFailures` | Applies | Applies |

`Connection` and `ReportsItemFailures` work as they do for a queue. [Queues](/azure/queue) covers
them. This application class settles failed messages one by one:

```csharp
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[ServiceBusModule(Subscription = "Orders", ReportsItemFailures = true)]
public partial class Application;
```

## Deploying

A topic function needs the topic, the subscription and the app setting that connects the function
app to the namespace
([Azure Service Bus trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-service-bus-trigger)):

| Item | Name |
|---|---|
| Topic | The name in `[Topic]` |
| Subscription | The module's `Subscription` |
| App setting | `AzureWebJobsServiceBus`, or the one that the module's `Connection` names |

The connection is set as it is for a queue. [Queues](/azure/queue) shows the commands.

These commands create the topic and the subscription for the template's app, in the resource group
`orders` and the namespace `orders-ns`:

```bash
az servicebus topic create \
    --resource-group orders \
    --namespace-name orders-ns \
    --name orders

az servicebus topic subscription create \
    --resource-group orders \
    --namespace-name orders-ns \
    --topic-name orders \
    --name Orders \
    --lock-duration PT1M \
    --max-delivery-count 10
```

`--lock-duration` and `--max-delivery-count` on the subscription set the lock duration and the
`MaxDeliveryCount` that its messages get. The second command writes their defaults, `PT1M` and
`10`. [az servicebus topic](https://learn.microsoft.com/en-us/cli/azure/servicebus/topic) and
[az servicebus topic subscription](https://learn.microsoft.com/en-us/cli/azure/servicebus/topic/subscription)
describe the two commands.

With an identity-based connection, the role assignment's scope has to include the subscription. A
role assignment on the topic alone causes an error
([Configure connections to remote services in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/manage-connections)).

The function app is created and deployed the same way for every trigger. The Azure
[Overview](/azure/) covers it.

## Testing

The template writes this test in `tests/Orders.Tests/OrderHandlerTests.cs`. The test sends a
message through `Application.Topics`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task ANotificationReachesTheHandler(Application.Topics topics, OrderLog log)
    {
        await topics.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: AzureFunctionsTesting]` beside
`[assembly: FunctionTesting]`. Under `[AzureFunctionsTesting]`, each façade call is one invocation
of the topic's function. The invocation carries one `ServiceBusReceivedMessage` for each message of
the call. Each message has these fields:

| Field | Value |
|---|---|
| Message id | The topic's name and the message's position, such as `orders-0` and `orders-1` |
| Delivery count | `1` |
| Content type | `application/json` |
| Application properties | None |

The test delivery does not read the subscription. Under `[FunctionTesting]` alone, the request has
no headers. The handler in [Body and headers](#body-and-headers) passes the template's test under
both attributes.

A failed message affects the call as it does for a queue, with `ReportsItemFailures` on or off.
[Queues](/azure/queue) covers it.

[Testing functions](/guide/testing-functions) covers the façades. The Azure
[Testing](/azure/testing) page covers `[AzureFunctionsTesting]`.

## Next

| Page | Covers |
|---|---|
| [Queues](/azure/queue) | The headers, failures, settlement and connection that a topic shares with a queue |
| [Triggers](/guide/triggers) | The trigger attributes and payload binding |
| [Overview](/azure/) | The packages, the entry point, running locally and deploying the function app |
| [Testing](/azure/testing) | What `[AzureFunctionsTesting]` builds |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
