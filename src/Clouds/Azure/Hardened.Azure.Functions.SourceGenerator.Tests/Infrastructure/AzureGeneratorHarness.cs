using System.Reflection;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.SourceGenerator;
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Shared;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.CodeAnalysis;
using Xunit;

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

    /// <summary>The modules each adapter package's targets bind, as the properties would name them.</summary>
    public const string ServiceBusModule = "Hardened.Azure.Functions.ServiceBus.ServiceBusModule";

    public const string TimerModule = "Hardened.Azure.Functions.Timer.TimerModule";

    public const string EventHubsModule = "Hardened.Azure.Functions.EventHubs.EventHubsModule";

    public const string CosmosDbModule = "Hardened.Azure.Functions.CosmosDb.CosmosDbModule";

    public const string BlobsModule = "Hardened.Azure.Functions.Blobs.BlobsModule";

    public const string EventGridModule = "Hardened.Azure.Functions.EventGrid.EventGridModule";

    public const string HttpModule = "Hardened.Azure.Functions.Http.HttpModule";

    /// <summary>
    /// One type per assembly the generated code touches. The Azure runtime brings the worker, and
    /// each adapter brings its extension and the SDK behind it, transitively; the web runtime
    /// brings the verbs.
    /// </summary>
    public static readonly Type[] Anchors = [
        typeof(HardenedFunctionAttribute),
        typeof(QueueAttribute),
        typeof(global::Hardened.Web.Runtime.Attributes.GetAttribute),
        typeof(global::Hardened.Requests.Runtime.Execution.BaseExecutionHandler<>),
        typeof(global::Hardened.Shared.Runtime.Attributes.HardenedModuleAttribute),
        typeof(FunctionsInvocationHandler),
        typeof(ServiceBusAdapter),
        typeof(global::Hardened.Azure.Functions.Timer.TimerAdapter),
        typeof(global::Hardened.Azure.Functions.EventHubs.EventHubsAdapter),
        typeof(global::Hardened.Azure.Functions.CosmosDb.CosmosDbAdapter),
        typeof(global::Hardened.Azure.Functions.Blobs.BlobsAdapter),
        typeof(global::Hardened.Azure.Functions.EventGrid.EventGridAdapter),
        typeof(global::Hardened.Azure.Functions.Http.HttpAdapter)
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
            using Hardened.Web.Runtime.Attributes;

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

    /// <summary>
    /// The generated provider, loaded from the compiled output and constructed, so a test reads
    /// what the worker would answer the host with rather than the text it was written from.
    /// </summary>
    public static IFunctionMetadataProvider Provider(GeneratorResult result) {
        using var stream = new MemoryStream();

        var emitted = result.Compilation.Emit(stream);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        var assembly = Assembly.Load(stream.ToArray());
        var type = assembly.GetType("TestApp.Generated.TestApplicationAzureFunctionMetadataProvider");

        Assert.NotNull(type);

        return (IFunctionMetadataProvider)Activator.CreateInstance(type)!;
    }
}
