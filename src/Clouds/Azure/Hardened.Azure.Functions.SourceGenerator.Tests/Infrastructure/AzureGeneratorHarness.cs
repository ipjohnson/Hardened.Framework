using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.SourceGenerator;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Azure.Functions.SourceGenerator.Tests.Infrastructure;

/// <summary>
/// Drives the function-handler pipeline, from the copy of Hardened.SourceGenerator compiled into
/// the Azure generator.
/// </summary>
/// <remarks>
/// The real build runs three generators over an application: the function generator compiles the
/// handlers, the library generator binds their modules, and the Azure generator writes the worker's
/// view. Running all three here is what keeps the test arrangement honest about that - a shim that
/// calls a handler invoker the function generator did not write would compile in neither.
/// </remarks>
public class FunctionDriver : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        global::Hardened.SourceGenerator.Function.FunctionIncrementalGenerator.Setup(context, applicationModel);
    }
}

/// <summary>Drives the trigger-to-module binding, which the library generator owns in a real build.</summary>
public class TriggerDriver : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            EntryPointSelector.UsingAttribute(),
            EntryPointSelector.TransformModel(false)
        ).WithComparer(new EntryPointSelector.Comparer());

        TriggerModuleGenerator.Setup(context, applicationModel);
    }
}

/// <summary>
/// The reference set the generated worker code binds against, and the source shape every test
/// goes through.
/// </summary>
public static class AzureGeneratorHarness {

    /// <summary>The module the Service Bus package's targets bind, as the property would name it.</summary>
    public const string ServiceBusModule = "Hardened.Azure.Functions.ServiceBus.ServiceBusModule";

    /// <summary>
    /// One type per assembly the generated code touches. The Azure runtime brings the worker and
    /// the Service Bus adapter brings the extension and the Service Bus SDK, transitively.
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(HardenedFunctionAttribute),
        typeof(QueueAttribute),
        typeof(global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<>),
        typeof(global::Hardened.Shared.Runtime.Attributes.HardenedModuleAttribute),
        typeof(FunctionsInvocationHandler),
        typeof(ServiceBusAdapter)
    ];

    /// <summary>
    /// An application entry point beside a handler class holding <paramref name="handlers"/>, run
    /// through the three generators with the build properties a referenced runtime would set.
    /// </summary>
    public static GeneratorResult Generate(string handlers, params (string Property, string Module)[] properties) =>
        Generate(handlers, properties, application: "", extraTypes: "");

    /// <summary>
    /// The same, with attributes written on the application beside <c>[HardenedModule]</c> - a
    /// module attribute carrying deployment settings - and any further types the source needs.
    /// </summary>
    public static GeneratorResult Generate(
        string handlers,
        (string Property, string Module)[] properties,
        string application = "",
        string extraTypes = "") {
        var source = $$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Functions.Runtime.Attributes;

            namespace TestApp;

            public class Order {
                public string Id { get; set; } = "";
            }

            {{extraTypes}}

            [HardenedModule]
            {{application}}
            public partial class TestApplication { }

            public class Handlers {
            {{handlers}}
            }
            """;

        return GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            [new AzureFunctionsSourceGenerator(), new FunctionDriver(), new TriggerDriver()],
            Anchors,
            buildProperties: properties.ToDictionary(pair => pair.Property, pair => pair.Module));
    }

    /// <summary>The shims, the provider and the executor, or an empty string when none were written.</summary>
    public static string FunctionsSource(GeneratorResult result) =>
        result.GeneratedSources.TryGetValue("TestApplication.AzureFunctions.cs", out var source) ? source : "";

    /// <summary>The entry point's worker registration, or an empty string when none was written.</summary>
    public static string WorkerSource(GeneratorResult result) =>
        result.GeneratedSources.TryGetValue("TestApplication.AzureFunctionsWorker.cs", out var source) ? source : "";
}
