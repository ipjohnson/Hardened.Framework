using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hardened.SourceGenerator.Shared;

/// <summary>
/// Registers the payload adapter each trigger in the project needs.
/// </summary>
/// <remarks>
/// <para>
/// A handler writes <c>[Queue("orders-new")]</c> and gets the SQS adapter. Which adapter that is
/// belongs to the runtime package rather than to this generator: the package ships a
/// <c>build/*.targets</c> declaring <c>HardenedQueueModule</c>, and this reads it. That is what
/// keeps the same handler working on another provider by changing one package reference, and it is
/// why nothing here names a cloud.
/// </para>
/// <para>
/// <b>An MSBuild property cannot name the attribute to look for.</b> A generator registers its
/// scans in <c>Initialize</c>, and <c>AnalyzerConfigOptionsProvider</c> is a pipeline value that has
/// not been read at that point. It does not need to: the trigger attributes belong to
/// <c>Hardened.Functions.Runtime</c>, so their names are constants here - and they have to be,
/// because an attribute a provider invented would put that provider's name in the handler.
/// </para>
/// <para>
/// <b>The registration is a static field on a partial of the entry point.</b> Emitting
/// <c>[SqsModule]</c> onto the application class would not work: a generator's regular output is
/// invisible to other generators, so the DependencyModules generator would never see the attribute.
/// <c>DependencyRegistry&lt;TApplication&gt;.AddModule</c> is the seam that needs no such
/// cooperation - the generated <c>InternalGetModules</c> already combines what the attributes
/// declared with whatever was added there.
/// </para>
/// <para>
/// A field rather than a method, because a field initializer needs nothing to call it. Static field
/// initializers across every partial of a type merge into that type's static constructor, so this
/// runs before the first instance exists and cannot collide with the empty <c>static Application()</c>
/// the DependencyModules generator already emits - which a second static constructor would.
/// </para>
/// </remarks>
public static class TriggerModuleGenerator {

    /// <summary>
    /// One trigger: the attribute a handler writes, and the property naming what serves it.
    /// </summary>
    public sealed class Trigger {
        public Trigger(
            string name, string attribute, string property, string scheme,
            bool namesItsOwnRoute = true, bool isFunctionHandler = true) {
            NamesItsOwnRoute = namesItsOwnRoute;
            IsFunctionHandler = isFunctionHandler;
            Name = name;
            Attribute = attribute;
            Property = property;
            Scheme = scheme;
            Spellings = Names(attribute);
            Type = TypeDefinition.Get(
                attribute.Substring(0, attribute.LastIndexOf('.')),
                attribute.Substring(attribute.LastIndexOf('.') + 1));
        }

        public string Name { get; }

        /// <summary>
        /// The scheme this trigger's routes are registered under - <c>QUEUE</c>, <c>TOPIC</c>,
        /// <c>TIMER</c>, <c>EVENT</c> - which is what the adapter puts on the request it builds.
        /// The two have to agree or a delivered message finds no handler.
        /// </summary>
        public string Scheme { get; }

        /// <summary>The attribute as a type, for the selector that finds handlers carrying it.</summary>
        public ITypeDefinition Type { get; }

        /// <summary>
        /// Whether the attribute's arguments are the route.
        /// </summary>
        /// <remarks>
        /// True for a source-named trigger - <c>[Queue("orders-new")]</c> is <c>QUEUE /orders-new</c>.
        /// False for <c>[HardenedFunction]</c>, whose argument is optional and whose route falls
        /// back to the method's own name, so it keeps its own derivation and appears here only to
        /// be bound to a module.
        /// </remarks>
        public bool NamesItsOwnRoute { get; }

        /// <summary>
        /// Whether the function generator compiles handlers carrying this attribute.
        /// </summary>
        /// <remarks>
        /// False for the web verbs, which the web generator owns. They appear here only to bind a
        /// module: <c>[Get]</c> has to reach an API Gateway adapter on Lambda and a Kestrel host
        /// elsewhere without the handler naming either.
        /// </remarks>
        public bool IsFunctionHandler { get; }

        public string Attribute { get; }

        public string Property { get; }

        /// <summary>
        /// Every way the attribute can be written at a use site: bare, with the suffix, and fully
        /// qualified. Matched on syntax rather than resolved through a symbol, which is what
        /// <see cref="SyntaxSelector{T}"/> does and for the same reason - the predicate runs on
        /// every attributed method in the project and a semantic lookup there is not free.
        /// </summary>
        public IReadOnlyList<string> Spellings { get; }

        private static IReadOnlyList<string> Names(string fullName) {
            var simple = fullName.Substring(fullName.LastIndexOf('.') + 1);

            var names = new List<string> { fullName, simple };

            if (simple.EndsWith("Attribute", StringComparison.Ordinal)) {
                names.Add(simple.Substring(0, simple.Length - "Attribute".Length));
            }

            return names;
        }
    }

    /// <summary>
    /// The triggers, and the property that binds each. Fixed here because they are the framework's
    /// own; what serves them is not.
    /// </summary>
    public static readonly IReadOnlyList<Trigger> Triggers = new[] {
        new Trigger("Queue", "Hardened.Functions.Runtime.Attributes.QueueAttribute", "HardenedQueueModule", "QUEUE"),
        new Trigger("Topic", "Hardened.Functions.Runtime.Attributes.TopicAttribute", "HardenedTopicModule", "TOPIC"),
        new Trigger("Timer", "Hardened.Functions.Runtime.Attributes.TimerAttribute", "HardenedTimerModule", "TIMER"),
        new Trigger("Event", "Hardened.Functions.Runtime.Attributes.EventAttribute", "HardenedEventModule", "EVENT"),

        // The two ordered sources. One trigger each rather than one shared "stream", because two
        // adapter packages binding a single property resolve first-import-wins on an import order
        // nobody controls - and because a change feed's handler binds a row image where a stream's
        // binds the publisher's own bytes.
        new Trigger("Change", "Hardened.Functions.Runtime.Attributes.ChangeAttribute", "HardenedChangeModule", "CHANGE"),
        new Trigger("Stream", "Hardened.Functions.Runtime.Attributes.StreamAttribute", "HardenedStreamModule", "STREAM"),

        // Unordered, unlike the two above: two notifications for one key can arrive out of order,
        // so this is a queue's failure shape rather than a shard's.
        new Trigger("Blob", "Hardened.Functions.Runtime.Attributes.BlobAttribute", "HardenedBlobModule", "BLOB"),

        // Not a trigger in the same sense - nothing delivers to it, a caller invokes it - but it
        // binds a module the same way, and for the same reason: an application that had to write
        // [InvokeModule] itself would name a cloud in the one file that must not.
        new Trigger("HardenedFunction", "Hardened.Requests.Abstract.Attributes.HardenedFunctionAttribute",
            "HardenedInvokeModule", "INVOKE", namesItsOwnRoute: false),

        // The web verbs, which the web generator routes and this only binds. All five name one
        // module, so a controller with a GET and a POST registers one adapter.
        new Trigger("Get", "Hardened.Web.Runtime.Attributes.GetAttribute",
            "HardenedHttpModule", "GET", namesItsOwnRoute: false, isFunctionHandler: false),
        new Trigger("Post", "Hardened.Web.Runtime.Attributes.PostAttribute",
            "HardenedHttpModule", "POST", namesItsOwnRoute: false, isFunctionHandler: false),
        new Trigger("Put", "Hardened.Web.Runtime.Attributes.PutAttribute",
            "HardenedHttpModule", "PUT", namesItsOwnRoute: false, isFunctionHandler: false),
        new Trigger("Patch", "Hardened.Web.Runtime.Attributes.PatchAttribute",
            "HardenedHttpModule", "PATCH", namesItsOwnRoute: false, isFunctionHandler: false),
        new Trigger("Delete", "Hardened.Web.Runtime.Attributes.DeleteAttribute",
            "HardenedHttpModule", "DELETE", namesItsOwnRoute: false, isFunctionHandler: false)
    };

    /// <summary>
    /// A trigger is used and nothing declares what serves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler would compile, deploy, and never be invoked. The message names the property to
    /// set, because the two ways to reach this are a missing runtime package and a provider that
    /// has no source of this kind - and the fix differs.
    /// </para>
    /// <para>
    /// Built per call rather than held in a static field, which is what analyzer release tracking
    /// looks for: a <c>public static readonly DiagnosticDescriptor</c> raises RS2008 in the
    /// generator projects that link this file and set <c>EnforceExtendedAnalyzerRules</c>.
    /// </para>
    /// </remarks>
    private static DiagnosticDescriptor NoModuleForTrigger() =>
        new(id: "HRDF001",
            title: "No adapter is registered for this trigger",
            messageFormat:
            "Handlers in this project use [{0}], but no referenced runtime declares a module for " +
            "it. Reference a runtime package that supports {0} triggers, or set <{1}> to the " +
            "module that should serve them.",
            category: "Hardened.Function",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    /// <summary>
    /// An adapter is registered for a trigger nothing in the project writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The counterpart to <see cref="NoModuleForTrigger"/>, and the reason a meta package is safe
    /// to publish. Referencing <c>Hardened.Aws.Lambda</c> binds every trigger at once, so a
    /// function that handles one queue carries the API Gateway and SNS adapters and their event
    /// assemblies into its deployment bundle. Nothing else can see that: the generator is the only
    /// thing that knows both which bindings are set and which triggers are used.
    /// </para>
    /// <para>
    /// Info rather than a warning. Referencing everything is a legitimate choice, and a project
    /// that grows a <c>[Topic]</c> handler next week was right all along - this is a note about
    /// bundle size, not a defect.
    /// </para>
    /// </remarks>
    private static DiagnosticDescriptor UnusedAdapter() =>
        new(id: "HRDF003",
            title: "An adapter is registered for a trigger this project does not use",
            messageFormat:
            "{0} is bound to serve {1} and nothing in this project declares one, so it ships in " +
            "the deployment bundle unreachable. Reference the adapter packages for the triggers " +
            "this project uses rather than a meta package, or clear {2}.",
            category: "Hardened.Function",
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

    public static void Setup(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {

        // One provider rather than one ForAttributeWithMetadataName per trigger. Four of those
        // would each need combining into the output, and the tuple that produces is four levels
        // deep - the shape WebIncrementalGenerator records as already near-unreadable at three.
        var used = context.SyntaxProvider
            .CreateSyntaxProvider(Uses, Named)
            .Where(name => name != null)
            .Collect()
            .Select((names, _) =>
                names.Where(name => name != null).Distinct().OrderBy(name => name, StringComparer.Ordinal)
                    .ToImmutableArray());

        var modules = context.AnalyzerConfigOptionsProvider.Select(ReadModules);

        context.RegisterSourceOutput(
            entryPointProvider.Combine(used).Combine(modules),
            SourceGeneratorWrapper.Wrap<
                ((EntryPointSelector.Model Entry, ImmutableArray<string?> Used) Left,
                 ImmutableArray<string?> Right)>(Generate));
    }

    private static bool Uses(SyntaxNode node, CancellationToken token) =>
        node is MethodDeclarationSyntax { AttributeLists.Count: > 0 };

    /// <summary>The trigger this method carries, or null for the many that carry none.</summary>
    private static string? Named(GeneratorSyntaxContext context, CancellationToken token) {
        foreach (var attribute in context.Node.DescendantNodes().OfType<AttributeSyntax>()) {
            var written = attribute.Name.ToString();

            foreach (var trigger in Triggers) {
                if (trigger.Spellings.Contains(written)) {
                    return trigger.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The module named for each trigger, in <see cref="Triggers"/> order, or null where the
    /// property is unset.
    /// </summary>
    /// <remarks>
    /// Selected into a plain array rather than combined as a provider, because
    /// <c>AnalyzerConfigOptionsProvider</c> hands back a new instance every run and combining it
    /// directly would regenerate on every keystroke.
    /// </remarks>
    private static ImmutableArray<string?> ReadModules(
        AnalyzerConfigOptionsProvider provider, CancellationToken token) =>
        Triggers.Select(trigger =>
            provider.GlobalOptions.TryGetValue("build_property." + trigger.Property, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null).ToImmutableArray();

    private static void Generate(
        SourceProductionContext context,
        ((EntryPointSelector.Model Entry, ImmutableArray<string?> Used) Left,
         ImmutableArray<string?> Right) models) {
        var entryPoint = models.Left.Entry;
        var used = models.Left.Used;
        var modules = models.Right;

        // No property set at all means no runtime package is referenced, which is an ordinary
        // state for a handler library and for a compilation under test. The diagnostic is for the
        // other case: a runtime is present and does not serve one of the triggers used. Reporting
        // "nothing binds [HardenedFunction]" at every project that has not referenced a cloud yet
        // would be noise rather than a finding.
        if (modules.All(module => module == null)) {
            return;
        }

        ReportUnused(context, used, modules);

        var register = new List<string>();

        for (var index = 0; index < Triggers.Count; index++) {
            var trigger = Triggers[index];

            if (!used.Contains(trigger.Name)) {
                continue;
            }

            var module = modules[index];

            if (module == null) {
                context.ReportDiagnostic(
                    Diagnostic.Create(NoModuleForTrigger(), Location.None, trigger.Name, trigger.Property));

                continue;
            }

            // An application that applied the module itself keeps its own. Modules deduplicate by
            // equality and several carry settings, so adding a second default-constructed one is
            // not harmless: SqsModule compares by type, so the inferred instance ties with the
            // declared one and whichever is reached first wins - which silently dropped
            // ReportBatchItemFailures the moment a deployment turned it on.
            if (Declares(entryPoint, module)) {
                continue;
            }

            // The same module can serve two triggers - a schedule and a bus event are one adapter -
            // and adding it twice would register the adapter twice.
            if (!register.Contains(module)) {
                register.Add(module);
            }
        }

        if (register.Count == 0) {
            return;
        }

        context.AddSource(
            entryPoint.EntryPointType.Name + ".TriggerModules.cs",
            GeneratedSource.Header(Source(entryPoint, register)));
    }

    /// <summary>
    /// Reports every bound module the project has no trigger for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grouped by module rather than reported per trigger, because several triggers share one.</b>
    /// The five web verbs all name <c>HardenedHttpModule</c>, and a schedule and a bus event are one
    /// adapter - so a project writing <c>[Get]</c> and no <c>[Post]</c>, or <c>[Timer]</c> and no
    /// <c>[Event]</c>, is carrying nothing it cannot reach. Reporting per trigger would fire on both
    /// and be wrong on both.
    /// </para>
    /// <para>
    /// Silent for a project that declares no handlers at all. That is an ordinary state for a
    /// handler library and for a compilation under test, and it is not what this is about.
    /// </para>
    /// </remarks>
    private static void ReportUnused(
        SourceProductionContext context,
        ImmutableArray<string?> used,
        ImmutableArray<string?> modules) {
        if (used.Length == 0) {
            return;
        }

        var reported = new List<string>();

        for (var index = 0; index < Triggers.Count; index++) {
            var module = modules[index];

            if (module == null || reported.Contains(module)) {
                continue;
            }

            var served = new List<Trigger>();

            for (var other = 0; other < Triggers.Count; other++) {
                if (modules[other] == module) {
                    served.Add(Triggers[other]);
                }
            }

            if (served.Any(trigger => used.Contains(trigger.Name))) {
                continue;
            }

            reported.Add(module);

            context.ReportDiagnostic(Diagnostic.Create(
                UnusedAdapter(), Location.None,
                module,
                Readable(served.Select(trigger => "[" + trigger.Name + "]")),
                Readable(served.Select(trigger => "<" + trigger.Property + ">").Distinct())));
        }
    }

    /// <summary>
    /// A list a sentence can contain: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.
    /// </summary>
    /// <remarks>
    /// The five web verbs make this worth having. A comma-joined list ended
    /// "clear &lt;HardenedTimerModule&gt;, &lt;HardenedEventModule&gt;", which reads as MSBuild a
    /// user could paste and is not.
    /// </remarks>
    private static string Readable(IEnumerable<string> parts) {
        var all = parts.ToList();

        return all.Count switch {
            0 => "",
            1 => all[0],
            _ => string.Join(", ", all.Take(all.Count - 1)) + " and " + all[all.Count - 1]
        };
    }

    /// <summary>
    /// Whether the entry point already applies this module as an attribute.
    /// </summary>
    /// <remarks>
    /// Compared on the attribute's own name, which DependencyModules generates as the module's name
    /// with <c>Attribute</c> appended. The module arrives here as a string from MSBuild and the
    /// attribute as a resolved type, so there is no symbol to compare - and a name is enough,
    /// because two modules with one name in one compilation would not compile either.
    /// </remarks>
    private static bool Declares(EntryPointSelector.Model entryPoint, string module) {
        var attribute = module.Substring(module.LastIndexOf('.') + 1) + "Attribute";

        return entryPoint.AttributeModels.Any(
            model => model.TypeDefinition.Name == attribute);
    }

    /// <summary>
    /// A field name nothing else will have chosen.
    /// </summary>
    /// <remarks>
    /// The entry point's own name is not enough on its own - it is a partial the author also
    /// writes - so the hash of its full name goes on the end. A collision would be a compile error
    /// rather than a silent problem, but it would be a confusing one in generated code.
    /// </remarks>
    private static string FieldName(EntryPointSelector.Model entryPoint) {
        var full = entryPoint.EntryPointType.Namespace + "." + entryPoint.EntryPointType.Name;

        var hash = 17;

        foreach (var character in full) {
            hash = unchecked(hash * 31 + character);
        }

        return "_hardenedTriggerModules_" + (hash & 0x7FFFFFF).ToString();
    }

    private static string Source(EntryPointSelector.Model entryPoint, IReadOnlyList<string> modules) {
        var file = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);

        var appClass = file.AddClass(entryPoint.EntryPointType.Name);

        appClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        // Written out fully qualified rather than assembled from type definitions. Generated code
        // has no usings to rely on, the shape is fixed, and the module names arrive as strings from
        // MSBuild - resolving them to symbols only to print them again would buy nothing.
        var registry =
            "global::DependencyModules.Runtime.Helpers.DependencyRegistry<global::" +
            entryPoint.EntryPointType.Namespace + "." + entryPoint.EntryPointType.Name + ">";

        var arguments = string.Join(", ", modules.Select(module => "new global::" + module + "()"));

        // AddModule returns an int nothing reads. Assigning it is the point: a field needs an
        // initializer to run, and this is the smallest expression that carries the call into one.
        var field = appClass.AddField(TypeDefinition.Get(typeof(int)), FieldName(entryPoint));

        field.Modifiers = ComponentModifier.Private | ComponentModifier.Static;

        field.InitializeValue =
            new CodeOutputComponent(registry + ".AddModule(" + arguments + ")") { Indented = false };

        var output = new OutputContext(new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global });

        file.WriteOutput(output);

        return output.Output();
    }
}
