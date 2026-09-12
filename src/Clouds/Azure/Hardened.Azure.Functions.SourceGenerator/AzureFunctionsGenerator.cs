using System.Collections.Immutable;
using System.Text;
using Hardened.SourceGenerator.Function;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// Writes the worker's view of an application: a <c>[Function]</c> shim per trigger handler or
/// per family, an <c>IFunctionMetadataProvider</c> describing them, and an
/// <c>IFunctionExecutor</c> that calls them.
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
/// written for a family only when the runtime package serving it is referenced, which is visible
/// here as the same <c>Hardened&lt;Trigger&gt;Module</c> property
/// <see cref="TriggerModuleGenerator"/> reads. An unbound trigger is that generator's HRDF001; a
/// bound family this generator has no Azure binding for is HRDAZ001 here, and a binding whose
/// setting the module did not supply is HRDAZ003.
/// </para>
/// <para>
/// <b>The function identity is the route</b> for every family that binds one entity: a queue, a
/// topic, a timer, a stream, a change feed, a blob container. Event Grid and HTTP deliver
/// everything to one function and the pipeline routes from the event or the request.
/// </para>
/// </remarks>
public static class AzureFunctionsGenerator {

    /// <summary>One trigger handler, reduced to what a shim needs to know about it.</summary>
    public sealed record Handler(string Scheme, string Path, string Owner);

    /// <summary>One function the generator will write.</summary>
    internal sealed class Function {
        public Function(string name, string source, AzureBinding binding, ModuleSettings settings, Handler? handler) {
            Name = name;
            Source = source;
            Binding = binding;
            Settings = settings;
            Handler = handler;
        }

        public string Name { get; }

        /// <summary>The source the handler named - <c>orders</c> for <c>[Queue("orders")]</c> - or empty for a family function.</summary>
        public string Source { get; }

        public AzureBinding Binding { get; }

        public ModuleSettings Settings { get; }

        /// <summary>The handler, for a per-source function; null for a family one.</summary>
        public Handler? Handler { get; }

        public string Scheme => Binding.Scheme;

        /// <summary>The route the shim hands over: the handler's, or empty where the adapter decides.</summary>
        public string Path => Handler?.Path ?? "";
    }

    /// <summary>One family this generator knows: the property that binds it and the scheme it routes under.</summary>
    private sealed class Family {
        public Family(string name, string property, string scheme) {
            Name = name;
            Property = property;
            Scheme = scheme;
        }

        public string Name { get; }

        public string Property { get; }

        public string Scheme { get; }
    }

    /// <summary>The families, in the order their functions are written.</summary>
    /// <remarks>
    /// The function triggers from the shared table, so a trigger added there is a family here, and
    /// the web verbs as one family under the property they all bind. HTTP is not a scheme in the
    /// handler model, so it is named directly.
    /// </remarks>
    private static readonly IReadOnlyList<Family> Families = BuildFamilies();

    private static IReadOnlyList<Family> BuildFamilies() {
        var families = new List<Family>();

        foreach (var trigger in TriggerModuleGenerator.Triggers) {
            if (trigger.IsFunctionHandler && trigger.NamesItsOwnRoute) {
                families.Add(new Family(trigger.Name, trigger.Property, trigger.Scheme));
            }
        }

        var web = TriggerModuleGenerator.Triggers.First(trigger => !trigger.IsFunctionHandler);

        families.Add(new Family("Http", web.Property, "HTTP"));

        return families;
    }

    /// <summary>Every spelling of a web verb attribute, for the syntax scan that finds them.</summary>
    private static readonly IReadOnlyList<string> WebVerbSpellings = TriggerModuleGenerator.Triggers
        .Where(trigger => !trigger.IsFunctionHandler)
        .SelectMany(trigger => trigger.Spellings)
        .ToList();

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

    private static DiagnosticDescriptor CollidingFunctionName() =>
        new(id: "HRDAZ002",
            title: "Two handlers produce the same function name",
            messageFormat:
            "The handlers {0} and {1} both produce the Azure function '{2}', which the host would " +
            "refuse as a duplicate. Rename one of their sources.",
            category: "Hardened.Azure",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <summary>
    /// A binding that needs a setting the module did not supply.
    /// </summary>
    /// <remarks>
    /// A Service Bus topic is read through a subscription and a Cosmos container lives in a
    /// database; neither is in the neutral trigger, because neither exists on the other providers.
    /// They are deployment facts, written on the module attribute, and a function cannot be
    /// described to the host without them.
    /// </remarks>
    private static DiagnosticDescriptor MissingSetting() =>
        new(id: "HRDAZ003",
            title: "A binding needs a setting the module did not supply",
            messageFormat:
            "The [{0}] handlers need {1} on [{2}], and this application does not set it. Write " +
            "[{2}({1} = \"...\")] on the application, beside [HardenedModule].",
            category: "Hardened.Azure",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <summary>
    /// A setting the metadata carries, written as something other than a literal.
    /// </summary>
    /// <remarks>
    /// The shim's attribute can carry any expression, but the host reads the metadata the provider
    /// returns, and the provider is written before anything is evaluated. A constant reference or a
    /// concatenation has no value at that point.
    /// </remarks>
    private static DiagnosticDescriptor SettingNotALiteral() =>
        new(id: "HRDAZ004",
            title: "A module setting has to be a literal",
            messageFormat:
            "{0} on [{1}] is written as {2}, and the function metadata the host indexes needs its " +
            "value at build. Write it as a string literal, an integer literal, or true or false.",
            category: "Hardened.Azure",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    public static void Setup(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {
        var selectors = TriggerModuleGenerator.Triggers
            .Where(trigger => trigger.IsFunctionHandler && trigger.NamesItsOwnRoute)
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

        // Whether any web verb is written, which is all the HTTP family needs to know: the web
        // generator routes the verbs and one catch-all function serves them.
        var usesWeb = context.SyntaxProvider
            .CreateSyntaxProvider(
                (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                (syntaxContext, _) => UsesWebVerb(syntaxContext.Node))
            .Where(uses => uses)
            .Collect()
            .Select((all, _) => all.Length > 0);

        // Selected into a comparable value rather than combined as a provider, for the reason
        // TriggerModuleGenerator gives: the options provider is a new instance every run.
        var modules = context.AnalyzerConfigOptionsProvider.Select(ReadModules);

        var assemblyName = context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName ?? "");

        context.RegisterSourceOutput(
            entryPointProvider.Combine(handlers).Combine(modules).Combine(assemblyName).Combine(usesWeb),
            SourceGeneratorWrapper.Wrap<
                ((((EntryPointSelector.Model Entry, Handlers Handlers) Left, Modules Modules) Left,
                  string AssemblyName) Left, bool UsesWeb)>(Generate));
    }

    private static bool UsesWebVerb(SyntaxNode node) {
        foreach (var attribute in node.DescendantNodes().OfType<AttributeSyntax>()) {
            if (WebVerbSpellings.Contains(attribute.Name.ToString())) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The module bound for each family in <see cref="Families"/> order, or null where the
    /// property is unset.
    /// </summary>
    private static Modules ReadModules(AnalyzerConfigOptionsProvider provider, CancellationToken token) =>
        new(Families.Select(family =>
            provider.GlobalOptions.TryGetValue("build_property." + family.Property, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null).ToImmutableArray());

    private static void Generate(
        SourceProductionContext context,
        ((((EntryPointSelector.Model Entry, Handlers Handlers) Left, Modules Modules) Left,
          string AssemblyName) Left, bool UsesWeb) models) {
        var entryPoint = models.Left.Left.Left.Entry;
        var handlers = models.Left.Left.Left.Handlers.All;
        var modules = models.Left.Left.Modules.All;
        var assemblyName = models.Left.AssemblyName;
        var usesWeb = models.UsesWeb;

        // No property set at all means no Azure runtime package is referenced, which is the state
        // of a handler library, of an application on another cloud, and of every compilation under
        // a generator test that names no runtime. Nothing to write.
        if (modules.All(module => module == null)) {
            return;
        }

        var functions = new List<Function>();
        var taken = new Dictionary<string, Function>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < Families.Count; index++) {
            context.CancellationToken.ThrowIfCancellationRequested();

            var family = Families[index];
            var module = modules[index];

            if (module == null) {
                // Unbound: TriggerModuleGenerator reports HRDF001 for it, once, with the property.
                continue;
            }

            var settings = ModuleSettings.For(entryPoint, module);

            // The HTTP family is declared by a web verb in this compilation, or by the module
            // written on the application: a host project whose routes live in a library names
            // [HttpModule] the way a Lambda host names [LambdaHttpModule], because a generator
            // sees only the compilation it runs in.
            var declared = family.Scheme == "HTTP"
                ? usesWeb || settings.Written
                : handlers.Any(handler => handler.Scheme == family.Scheme);

            if (!declared) {
                continue;
            }

            var binding = AzureBinding.For(family.Scheme);

            if (binding == null) {
                context.ReportDiagnostic(Diagnostic.Create(
                    NoAzureBinding(), Location.None, family.Name, family.Property));

                continue;
            }

            if (!SettingsAreUsable(context, family, module, binding, settings)) {
                continue;
            }

            if (!binding.PerSource) {
                var family1 = new Function(binding.FunctionPrefix, "", binding, settings, null);

                if (taken.TryGetValue(family1.Name, out var owner)) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CollidingFunctionName(), Location.None, owner.Handler?.Owner ?? owner.Name,
                        family.Name, family1.Name));

                    continue;
                }

                taken.Add(family1.Name, family1);
                functions.Add(family1);

                continue;
            }

            // Ordered, so the generated file and the metadata read the same way whatever order
            // the handlers were found in - which is what keeps the incremental output stable.
            foreach (var handler in handlers.Where(one => one.Scheme == family.Scheme)
                         .OrderBy(one => one.Path, StringComparer.Ordinal)
                         .ThenBy(one => one.Owner, StringComparer.Ordinal)) {
                var name = FunctionName(binding.FunctionPrefix, handler.Path);

                if (taken.TryGetValue(name, out var owner)) {
                    context.ReportDiagnostic(Diagnostic.Create(
                        CollidingFunctionName(), Location.None, owner.Handler?.Owner ?? owner.Name, handler.Owner, name));

                    continue;
                }

                var function = new Function(name, handler.Path.TrimStart('/'), binding, settings, handler);

                taken.Add(name, function);
                functions.Add(function);
            }
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

    /// <summary>
    /// Whether the module supplies what the family's binding needs, reporting what it does not.
    /// </summary>
    private static bool SettingsAreUsable(
        SourceProductionContext context, Family family, string module, AzureBinding binding, ModuleSettings settings) {
        var attribute = module.Substring(module.LastIndexOf('.') + 1);
        var usable = true;

        foreach (var required in binding.MissingSettings(settings)) {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingSetting(), Location.None, family.Name, required, attribute));

            usable = false;
        }

        foreach (var read in binding.ReadSettings) {
            var setting = settings.Get(read);

            if (setting != null && !setting.IsLiteral) {
                context.ReportDiagnostic(Diagnostic.Create(
                    SettingNotALiteral(), Location.None, read, attribute, setting.Text));

                usable = false;
            }
        }

        return usable;
    }

    /// <summary>
    /// A per-source function's name, which is also the shim method's: <c>Queue_orders</c>.
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
