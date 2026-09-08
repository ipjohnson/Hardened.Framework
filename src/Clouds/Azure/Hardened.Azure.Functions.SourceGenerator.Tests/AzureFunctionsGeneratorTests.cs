using System.Reflection;
using Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.CodeAnalysis;
using Xunit;
using static Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure.AzureGeneratorHarness;

namespace Hardened.Azure.Functions.SourceGenerator.Tests;

/// <summary>
/// What the Azure generator writes for a trigger handler, and when it writes nothing.
///
/// <para>
/// Every case that emits compiles what it emitted, against the worker and the runtime packages -
/// the rule from docs/design/testing-conventions.md. The provider is also run, because what it
/// returns is what the host indexes and a string assertion on it would prove only that the text
/// looked right.
/// </para>
/// </summary>
public class AzureFunctionsGeneratorTests {

    private const string QueueHandler = """
            [Queue("orders")]
            public void OnOrder(Order order) { }
        """;

    /// <summary>
    /// The three things the worker needs, from one handler: the shim carrying the real binding
    /// attribute, the provider, and the executor. Compiled, not matched.
    /// </summary>
    [Fact]
    public void AQueueHandlerCompilesToAShimAProviderAndAnExecutor() {
        var result = Generate(QueueHandler, ("HardenedQueueModule", ServiceBusModule)).AssertNoErrors();

        var functions = FunctionsSource(result);

        Assert.Contains("[global::Microsoft.Azure.Functions.Worker.Function(\"Queue_orders\")]", functions);
        Assert.Contains(
            "[global::Microsoft.Azure.Functions.Worker.ServiceBusTrigger(\"orders\", IsBatched = true)] " +
            "global::Azure.Messaging.ServiceBus.ServiceBusReceivedMessage[] messages",
            functions);
        Assert.Contains("class TestApplicationAzureFunctionMetadataProvider", functions);
        Assert.Contains("class TestApplicationAzureFunctionExecutor", functions);
    }

    /// <summary>
    /// The shim hands the invocation handler the route the handler was compiled under, which is
    /// what the pipeline dispatches on, with the batch and its settlement actions bundled and the
    /// trigger dispatch named. A shim carrying another route would invoke and route to nothing.
    /// </summary>
    [Fact]
    public void TheShimCarriesTheHandlersRoute() {
        var result = Generate(QueueHandler, ("HardenedQueueModule", ServiceBusModule)).AssertNoErrors();

        Assert.Contains(
            "FunctionsInvocationHandler.Invoke(context, \"QUEUE\", \"/orders\", " +
            "new global::Hardened.Azure.Functions.ServiceBus.ServiceBusDelivery(messages, messageActions), " +
            "global::Hardened.Azure.Functions.Runtime.Execution.FunctionsDispatch.Trigger)",
            FunctionsSource(result));
    }

    /// <summary>
    /// The registration the runtime calls through <c>UseHardened</c>: the entry point implements
    /// the interface and registers both generated types.
    /// </summary>
    [Fact]
    public void TheEntryPointRegistersTheProviderAndTheExecutor() {
        var result = Generate(QueueHandler, ("HardenedQueueModule", ServiceBusModule)).AssertNoErrors();

        var worker = WorkerSource(result);

        Assert.Contains("IHardenedFunctionsApplication", worker);
        Assert.Contains("TestApplicationAzureFunctionMetadataProvider", worker);
        Assert.Contains("TestApplicationAzureFunctionExecutor", worker);
    }

    /// <summary>
    /// The provider, run: it lists exactly the functions the handlers declare, no more and no
    /// fewer, each with the entry point and script file the host loads it by.
    /// </summary>
    [Fact]
    public async Task TheProviderListsExactlyTheFunctionsTheHandlersDeclare() {
        var result = Generate(
            """
                [Queue("orders")]
                public void OnOrder(Order order) { }

                [Queue("returns")]
                public void OnReturn(Order order) { }
            """,
            ("HardenedQueueModule", ServiceBusModule)).AssertNoErrors();

        var functions = await Provider(result).GetFunctionMetadataAsync("");

        Assert.Equal(["Queue_orders", "Queue_returns"], functions.Select(function => function.Name).Order());

        var orders = Assert.Single(functions, function => function.Name == "Queue_orders");

        Assert.Equal("TestApp.Generated.TestApplicationAzureFunctions.Queue_orders", orders.EntryPoint);
        Assert.Equal("GeneratorTestAssembly.dll", orders.ScriptFile);
        Assert.Equal("dotnet-isolated", orders.Language);
        Assert.Contains("\"queueName\":\"orders\"", Assert.Single(orders.RawBindings!));
    }

    /// <summary>
    /// A trigger nothing binds is the library generator's HRDF001, as it is on every provider.
    /// The Azure generator writes no shim for it and adds no diagnostic of its own, because the
    /// property named in HRDF001 is the whole of the fix.
    /// </summary>
    [Fact]
    public void AHandlerWithNoBoundModuleReportsHRDF001AsToday() {
        var result = Generate(
            """
                [Queue("orders")]
                public void OnOrder(Order order) { }

                [Topic("order-events")]
                public void OnEvent(Order order) { }
            """,
            ("HardenedQueueModule", ServiceBusModule));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, one => one.Id == "HRDF001");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("[Topic]", diagnostic.GetMessage());
        Assert.Contains("HardenedTopicModule", diagnostic.GetMessage());

        Assert.DoesNotContain("Topic_", FunctionsSource(result));
        Assert.DoesNotContain(result.GeneratorDiagnostics, one => one.Id.StartsWith("HRDAZ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A binding that needs a setting the module did not supply. A Service Bus topic is read
    /// through a subscription, which no neutral trigger names, so a topic handler without
    /// <c>[ServiceBusModule(Subscription = ...)]</c> on the application cannot be described to the
    /// host and is an error naming the setting and where to write it.
    /// </summary>
    [Fact]
    public void ATopicHandlerWithoutASubscriptionIsReported() {
        var result = Generate(
            """
                [Topic("order-events")]
                public void OnEvent(Order order) { }
            """,
            ("HardenedTopicModule", ServiceBusModule));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, one => one.Id == "HRDAZ003");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("[Topic]", diagnostic.GetMessage());
        Assert.Contains("Subscription", diagnostic.GetMessage());
        Assert.Contains("[ServiceBusModule(Subscription = \"...\")]", diagnostic.GetMessage());
        Assert.DoesNotContain("Topic_", FunctionsSource(result));
    }

    /// <summary>
    /// The subscription supplied on the module reaches both halves: the shim's attribute and the
    /// binding the host indexes.
    /// </summary>
    [Fact]
    public async Task ATopicHandlerWithASubscriptionCompilesToASubscriptionFunction() {
        var result = Generate(
            """
                [Topic("order-events")]
                public void OnEvent(Order order) { }
            """,
            [("HardenedTopicModule", ServiceBusModule)],
            application: "[global::Hardened.Azure.Functions.ServiceBus.ServiceBusModule(Subscription = \"orders-service\")]")
            .AssertNoErrors();

        Assert.Contains(
            "ServiceBusTrigger(\"order-events\", \"orders-service\", IsBatched = true)",
            FunctionsSource(result));

        var topic = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Equal("Topic_order_events", topic.Name);
        Assert.Contains("\"subscriptionName\":\"orders-service\"", Assert.Single(topic.RawBindings!));
    }

    /// <summary>
    /// A setting written as something the generator cannot evaluate. The shim could carry it; the
    /// metadata the host reads could not.
    /// </summary>
    [Fact]
    public void ASettingThatIsNotALiteralIsReported() {
        var result = Generate(
            QueueHandler,
            [("HardenedQueueModule", ServiceBusModule)],
            application: "[global::Hardened.Azure.Functions.ServiceBus.ServiceBusModule(Connection = Names.Connection)]",
            extraTypes: "public static class Names { public const string Connection = \"Bus\"; }");

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, one => one.Id == "HRDAZ004");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Connection", diagnostic.GetMessage());
    }

    /// <summary>
    /// Settling per message changes the function the host is told about, not the handler: the
    /// host's auto-completion is turned off on the binding so the adapter can complete what the
    /// handler accepted.
    /// </summary>
    [Fact]
    public async Task ReportingItemFailuresTurnsOffTheHostsAutoCompletion() {
        var result = Generate(
            QueueHandler,
            [("HardenedQueueModule", ServiceBusModule)],
            application: "[global::Hardened.Azure.Functions.ServiceBus.ServiceBusModule(ReportsItemFailures = true)]")
            .AssertNoErrors();

        Assert.Contains("IsBatched = true, AutoCompleteMessages = false", FunctionsSource(result));

        var queue = Assert.Single(await Provider(result).GetFunctionMetadataAsync(""));

        Assert.Contains("\"autoCompleteMessages\":false", Assert.Single(queue.RawBindings!));
    }

    /// <summary>
    /// Two sources whose names produce one function name. The host refuses duplicate function
    /// names, case-insensitively, so this is an error and only the first shim is written.
    /// </summary>
    [Fact]
    public void TwoSourcesProducingOneFunctionNameAreReported() {
        var result = Generate(
            """
                [Queue("orders-new")]
                public void Dashed(Order order) { }

                [Queue("orders.new")]
                public void Dotted(Order order) { }
            """,
            ("HardenedQueueModule", ServiceBusModule));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, one => one.Id == "HRDAZ002");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Queue_orders_new", diagnostic.GetMessage());

        var functions = FunctionsSource(result);

        Assert.Equal(1, Occurrences(functions, "[global::Microsoft.Azure.Functions.Worker.Function("));
    }

    /// <summary>
    /// No Azure runtime package referenced, no worker code: a handler library and an application
    /// on another cloud compile the same handlers and must not gain a dependency on the worker.
    /// </summary>
    [Fact]
    public void NoBoundModuleAtAllWritesNothing() {
        var result = Generate(QueueHandler).AssertNoErrors();

        Assert.Equal("", FunctionsSource(result));
        Assert.Equal("", WorkerSource(result));
    }

    /// <summary>
    /// An Azure runtime referenced and nothing it serves declared. The registration is still
    /// written, because <c>UseHardened</c> has to compile, and the provider honestly lists no
    /// function.
    /// </summary>
    [Fact]
    public async Task AnApplicationWithNoServedHandlerRegistersAnEmptyProvider() {
        var result = Generate("", ("HardenedQueueModule", ServiceBusModule)).AssertNoErrors();

        Assert.Contains("IHardenedFunctionsApplication", WorkerSource(result));
        Assert.Empty(await Provider(result).GetFunctionMetadataAsync(""));
    }

    /// <summary>
    /// The generated provider, loaded from the compilation the test built and instantiated, so
    /// what is asserted is what the worker would answer the host with.
    /// </summary>
    private static int Occurrences(string text, string fragment) {
        var count = 0;

        for (var index = text.IndexOf(fragment, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal)) {
            count++;
        }

        return count;
    }
}
