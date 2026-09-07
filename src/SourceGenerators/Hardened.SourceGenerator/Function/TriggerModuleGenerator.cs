using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hardened.SourceGenerator.Function;

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
        public Trigger(string name, string attribute, string property, string scheme) {
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
        new Trigger("Event", "Hardened.Functions.Runtime.Attributes.EventAttribute", "HardenedEventModule", "EVENT")
    };

    /// <summary>
    /// A trigger is used and nothing declares what serves it.
    /// </summary>
    /// <remarks>
    /// The handler would compile, deploy, and never be invoked. The message names the property to
    /// set, because the two ways to reach this are a missing runtime package and a provider that
    /// has no source of this kind - and the fix differs.
    /// </remarks>
    public static readonly DiagnosticDescriptor NoModuleForTrigger = new(
        "HRDF001",
        "No adapter is registered for this trigger",
        "Handlers in this project use [{0}], but no referenced runtime declares a module for it. " +
        "Reference a runtime package that supports {0} triggers, or set <{1}> to the module that " +
        "should serve them.",
        "Hardened.Function",
        DiagnosticSeverity.Error,
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

        var register = new List<string>();

        for (var index = 0; index < Triggers.Count; index++) {
            var trigger = Triggers[index];

            if (!used.Contains(trigger.Name)) {
                continue;
            }

            var module = modules[index];

            if (module == null) {
                context.ReportDiagnostic(
                    Diagnostic.Create(NoModuleForTrigger, Location.None, trigger.Name, trigger.Property));

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
