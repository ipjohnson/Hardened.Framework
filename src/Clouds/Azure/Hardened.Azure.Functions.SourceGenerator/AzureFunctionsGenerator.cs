using System.Collections.Immutable;
using System.Text;
using Hardened.SourceGenerator.Function;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// Writes the worker's view of an application: a <c>[Function]</c> shim per trigger handler, an
/// <c>IFunctionMetadataProvider</c> describing them, and an <c>IFunctionExecutor</c> that calls
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the application needs a generator at all.</b> The isolated worker discovers functions
/// from metadata. The Worker SDK's own generator writes that metadata from the <c>[Function]</c>
/// methods it can see, and it can see none of Hardened's: the handlers carry neutral triggers, and
/// a generator's output is invisible to every other generator. So this one writes three things
/// from the same handler model the function generator compiles - the shim the host invokes, the
/// provider the host indexes, and the executor that runs the shim without reflection.
/// </para>
/// <para>
/// <b>Which triggers get a function is decided the way the adapters are chosen.</b> A shim is
/// written for a handler only when the runtime package serving its trigger is referenced, which is
/// visible here as the same <c>Hardened&lt;Trigger&gt;Module</c> property
/// <see cref="TriggerModuleGenerator"/> reads. An unbound trigger is that generator's HRDF001; a
/// bound trigger this generator has no Azure binding for is HRDAZ001 here.
/// </para>
/// <para>
/// <b>The function identity is the route.</b> Every handler gets its own function, named from its
/// trigger and source - <c>Queue_orders</c> - so the host invokes exactly one shim per source and
/// the shim hands the invocation handler the route it was generated for. No payload is ever
/// inspected to find out where it goes.
/// </para>
/// </remarks>
public static class AzureFunctionsGenerator {

    /// <summary>One trigger handler, reduced to what a shim needs to know about it.</summary>
    public sealed record Handler(string Scheme, string Path, string Owner);

    /// <summary>One function the generator will write.</summary>
    internal sealed class Function {
        public Function(string name, Handler handler, AzureBinding binding) {
            Name = name;
            Handler = handler;
            Binding = binding;
        }

        public string Name { get; }

        public Handler Handler { get; }

        public AzureBinding Binding { get; }

        /// <summary>The source the handler named: <c>orders</c> for <c>[Queue("orders")]</c>.</summary>
        public string Source => Handler.Path.TrimStart('/');
    }

    /// <summary>
    /// A bound trigger this generator has no Azure binding for.
    /// </summary>
    /// <remarks>
    /// An error, because the handler would compile, deploy, and never be invoked: no shim means no
    /// function for the host to index. Built per call rather than held in a static field, which
    /// analyzer release tracking (RS2008) asks of a generator that links these files.
    /// </remarks>
    private static DiagnosticDescriptor NoAzureBinding() =>
        new(id: "HRDAZ001",
            title: "No Azure Functions binding exists for this trigger",
            messageFormat:
            "Handlers in this project use [{0}] and <{1}> is bound, but " +
            "Hardened.Azure.Functions.SourceGenerator has no Azure binding for {0} triggers, so no " +
            "function is generated for them and the host will never invoke them.",
            category: "Hardened.Azure",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <summary>
    /// Two handlers whose trigger and source produce one function name.
    /// </summary>
    /// <remarks>
    /// An error rather than a warning, unlike the façade collision HRDF002: the host refuses an
    /// application with two functions of one name, and it compares them case-insensitively, so
    /// this does too.
    /// </remarks>
    private static DiagnosticDescriptor CollidingFunctionName() =>
        new(id: "HRDAZ002",
            title: "Two handlers produce the same function name",
            messageFormat:
            "The handlers {0} and {1} both produce the Azure function '{2}', which the host would " +
            "refuse as a duplicate. Rename one of their sources.",
            category: "Hardened.Azure",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <summary>The triggers a shim can be written for: the ones that name their own route.</summary>
    private static readonly IReadOnlyList<TriggerModuleGenerator.Trigger> Triggers =
        TriggerModuleGenerator.Triggers
            .Where(trigger => trigger.IsFunctionHandler && trigger.NamesItsOwnRoute)
            .ToList();

    public static void Setup(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {
        var selectors = Triggers
            .Select(trigger => new SyntaxSelector<MethodDeclarationSyntax>(trigger.Type))
            .ToArray();

        var modelGenerator = new FunctionModelGenerator();

        // The function generator's own model, reduced to the route and an owner for diagnostics
        // before it enters the pipeline. The full model carries symbols and schemas that cannot
        // compare by value, and a shim depends on none of them.
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                (node, token) => selectors.Any(selector => selector.Where(node, token)),
                (syntaxContext, token) => {
                    var model = modelGenerator.GenerateRequestModel(syntaxContext, token);

                    return new Handler(
                        model.Name.Method,
                        model.Name.Path,
                        model.ControllerType.Name + "." + model.HandlerMethod);
                })
            .Collect()
            .Select((all, _) => new Handlers(all));

        // Selected into a comparable value rather than combined as a provider, for the reason
        // TriggerModuleGenerator gives: the options provider is a new instance every run.
        var modules = context.AnalyzerConfigOptionsProvider.Select(ReadModules);

        var assemblyName = context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName ?? "");

        context.RegisterSourceOutput(
            entryPointProvider.Combine(handlers).Combine(modules).Combine(assemblyName),
            SourceGeneratorWrapper.Wrap<
                (((EntryPointSelector.Model Entry, Handlers Handlers) Left, Modules Modules) Left,
                 string AssemblyName)>(Generate));
    }

    /// <summary>
    /// The module bound for each trigger in <see cref="Triggers"/> order, or null where the
    /// property is unset.
    /// </summary>
    private static Modules ReadModules(AnalyzerConfigOptionsProvider provider, CancellationToken token) =>
        new(Triggers.Select(trigger =>
            provider.GlobalOptions.TryGetValue("build_property." + trigger.Property, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null).ToImmutableArray());

    private static void Generate(
        SourceProductionContext context,
        (((EntryPointSelector.Model Entry, Handlers Handlers) Left, Modules Modules) Left,
         string AssemblyName) models) {
        var entryPoint = models.Left.Left.Entry;
        var handlers = models.Left.Left.Handlers.All;
        var modules = models.Left.Modules.All;
        var assemblyName = models.AssemblyName;

        // No property set at all means no Azure runtime package is referenced, which is the state
        // of a handler library, of an application on another cloud, and of every compilation under
        // a generator test that names no runtime. Nothing to write.
        if (modules.All(module => module == null)) {
            return;
        }

        var functions = new List<Function>();
        var taken = new Dictionary<string, Function>(StringComparer.OrdinalIgnoreCase);
        var reported = new HashSet<string>(StringComparer.Ordinal);

        // Ordered, so the generated file and the metadata read the same way whatever order the
        // handlers were found in - which is what keeps the incremental output stable.
        foreach (var handler in handlers.OrderBy(one => one.Scheme, StringComparer.Ordinal)
                     .ThenBy(one => one.Path, StringComparer.Ordinal)
                     .ThenBy(one => one.Owner, StringComparer.Ordinal)) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var index = IndexOfTrigger(handler.Scheme);

            // A scheme no trigger names its route under is not a shim's business.
            if (index < 0 || modules[index] == null) {
                // Unbound: TriggerModuleGenerator reports HRDF001 for it, once, with the property.
                continue;
            }

            var binding = AzureBinding.For(handler.Scheme);

            if (binding == null) {
                if (reported.Add(handler.Scheme)) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        NoAzureBinding(), Location.None, Triggers[index].Name, Triggers[index].Property));
                }

                continue;
            }

            var name = FunctionName(binding.FunctionPrefix, handler.Path);

            if (taken.TryGetValue(name, out var owner)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    CollidingFunctionName(), Location.None, owner.Handler.Owner, handler.Owner, name));

                continue;
            }

            var function = new Function(name, handler, binding);

            taken.Add(name, function);
            functions.Add(function);
        }

        // Written even with nothing in it. An application whose only handlers are of a kind this
        // generator does not serve still has to satisfy UseHardened's constraint, and the worker
        // answering the host with no functions is the honest description of it.
        var emitted = AzureFunctionsEmitter.Emit(entryPoint, functions, assemblyName);

        context.AddSource(
            entryPoint.EntryPointType.Name + ".AzureFunctions.cs",
            GeneratedSource.Header(emitted.Functions));

        context.AddSource(
            entryPoint.EntryPointType.Name + ".AzureFunctionsWorker.cs",
            GeneratedSource.Header(emitted.Worker));
    }

    private static int IndexOfTrigger(string scheme) {
        for (var index = 0; index < Triggers.Count; index++) {
            if (Triggers[index].Scheme == scheme) {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The function's name, which is also the shim method's: <c>Queue_orders</c>.
    /// </summary>
    /// <remarks>
    /// One name for both, so what the host logs is what a stack trace shows. A function name may
    /// carry letters, digits, underscores and hyphens and a method name may not carry a hyphen, so
    /// everything outside letters, digits and underscores becomes an underscore - which is why two
    /// sources can collide, and why HRDAZ002 exists.
    /// </remarks>
    internal static string FunctionName(string prefix, string path) {
        var builder = new StringBuilder(prefix).Append('_');

        foreach (var character in path.TrimStart('/')) {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The handlers as a value, so the pipeline compares what was found rather than the array it
    /// was found in.
    /// </summary>
    public sealed class Handlers : IEquatable<Handlers> {
        public Handlers(ImmutableArray<Handler> all) {
            All = all;
        }

        public ImmutableArray<Handler> All { get; }

        public bool Equals(Handlers? other) =>
            other != null && All.SequenceEqual(other.All);

        public override bool Equals(object? obj) => Equals(obj as Handlers);

        public override int GetHashCode() {
            var hash = 17;

            foreach (var handler in All) {
                hash = unchecked(hash * 31 + handler.GetHashCode());
            }

            return hash;
        }
    }

    /// <summary>The bound modules as a value, for the same reason.</summary>
    public sealed class Modules : IEquatable<Modules> {
        public Modules(ImmutableArray<string?> all) {
            All = all;
        }

        public ImmutableArray<string?> All { get; }

        public bool Equals(Modules? other) =>
            other != null && All.SequenceEqual(other.All, StringComparer.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as Modules);

        public override int GetHashCode() {
            var hash = 17;

            foreach (var module in All) {
                hash = unchecked(hash * 31 + (module?.GetHashCode() ?? 0));
            }

            return hash;
        }
    }
}
