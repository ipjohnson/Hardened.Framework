using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Function;

/// <summary>
/// The trigger façades a test sends through.
/// </summary>
/// <remarks>
/// <para>
/// One nested class per trigger kind on the entry point - <c>Queues</c>, <c>Topics</c>,
/// <c>Timers</c> - with a method per source taking the payload type the handler binds. A test
/// writes <c>queues.SendTo.OrdersNew(new Order(...))</c> and the compiler checks both halves: the
/// queue exists because the method does, and the payload matches because overload resolution says
/// so.
/// </para>
/// <para>
/// <b>One class per kind rather than one per source, and that is what makes the collision safe.</b>
/// A queue called <c>orders</c> and a topic called <c>orders</c> both become a method called
/// <c>Orders</c>; on one combined façade they would be overloads separated only by payload type,
/// so a test could not say which it meant. Split by kind they sit on different objects.
/// </para>
/// <para>
/// Nothing in the application references these, so a published function trims them out entirely.
/// That only holds while the testing package resolves them and the application registers nothing -
/// a DI registration is exactly what a trimmer cannot remove.
/// </para>
/// <para>
/// <b>The façade references nothing but the BCL and the application's own payload types.</b> The
/// delegate it calls is a <c>Func</c>, not a framework interface, so a production assembly carrying
/// these takes no package dependency for them - which is what lets the trimming claim stand without
/// a caveat. An interface here would have meant every project with a queue handler referencing a
/// testing package.
/// </para>
/// <para>
/// Written as text rather than through CSharpAuthor. The shape is fixed and small, and the two
/// things it needs - an array parameter and <c>params</c> - are the two the emitter has no vocabulary
/// for, so building it through the emitter would be more code that says less.
/// </para>
/// </remarks>
public static class TriggerFacadeGenerator {

    /// <summary>The kinds that get a façade, what a test calls to reach one, and what to call it.</summary>
    private static readonly (string Scheme, string Facade, string Noun)[] Kinds = {
        ("QUEUE", "Queues", "queue"),
        ("TOPIC", "Topics", "topic"),
        ("TIMER", "Timers", "timer")
    };

    /// <summary>
    /// Two sources of one kind whose names produce the same façade method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning rather than an error, because the application itself is correct: both handlers
    /// route, and only the test façade is short one method. Failing a build over a testing
    /// convenience would be disproportionate.
    /// </para>
    /// <para>
    /// But not silent either, which is what it was. A test calling the surviving method looks like
    /// it covers both sources and covers one, and nothing in the test says which - that is a worse
    /// outcome than a missing method, because it reads as coverage that is not there.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CollidingFacadeName = new(
        "HRDF002",
        "Two sources produce the same test method name",
        "The {0}s '{1}' and '{2}' both produce the test method '{3}', so only '{2}' can be reached " +
        "through a trigger façade. Rename one of them, or suppress HRDF002 to keep the collision.",
        "Hardened.Function",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The façades for one application, or null when it declares no trigger that has one.
    /// </summary>
    public static string? Generate(
        SourceProductionContext context,
        EntryPointSelector.Model entryPoint,
        IReadOnlyList<RequestHandlerModel> handlers,
        CancellationToken cancellationToken) {

        var bodies = new List<string>();

        foreach (var kind in Kinds) {
            cancellationToken.ThrowIfCancellationRequested();

            var forKind = handlers.Where(handler => handler.Name.Method == kind.Scheme).ToList();

            if (forKind.Count > 0) {
                bodies.Add(Facade(context, kind.Scheme, kind.Facade, kind.Noun, forKind));
            }
        }

        if (bodies.Count == 0) {
            return null;
        }

        return $@"namespace {entryPoint.EntryPointType.Namespace}
{{
    public partial class {entryPoint.EntryPointType.Name}
    {{
{string.Join("\n", bodies)}    }}
}}
";
    }

    private static string Facade(
        SourceProductionContext context, string scheme, string name, string noun,
        IReadOnlyList<RequestHandlerModel> handlers) {
        var methods = new StringBuilder();
        var taken = new Dictionary<string, string>();

        foreach (var handler in handlers) {
            var source = handler.Name.Path.TrimStart('/');
            var methodName = Identifier(source);

            // A collision inside one kind is two sources a test could not tell apart, and unlike
            // the cross-kind case there is no second façade to separate them. Emitting both would
            // be a duplicate method, so the second is skipped and reported - both still route
            // correctly at run time, and only one is reachable by name from a test.
            if (taken.TryGetValue(methodName, out var owner)) {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        CollidingFacadeName, Location.None, noun, source, owner, methodName));

                continue;
            }

            taken.Add(methodName, source);

            methods.Append(Method(methodName, scheme, handler));
        }

        return $@"        public class {name}
        {{
            private readonly global::System.Func<object, string, string, global::System.Threading.Tasks.Task> _invoke;

            public {name}(global::System.Func<object, string, string, global::System.Threading.Tasks.Task> invoke)
            {{
                _invoke = invoke;
            }}
{methods}        }}
";
    }

    private static string Method(string methodName, string scheme, RequestHandlerModel handler) {
        var payload = handler.RequestParameterInformationList
            .FirstOrDefault(parameter => parameter.BindingType == ParameterBindType.Body);

        var route = $"\"{scheme}\", \"{handler.Name.Path}\"";

        if (payload == null) {
            // A schedule, or a handler that takes nothing. No message to send, so no parameter -
            // which is the whole reason timers get a façade shape of their own.
            return $@"
            public global::System.Threading.Tasks.Task {methodName}() =>
                _invoke(global::System.Array.Empty<object>(), {route});
";
        }

        var type = "global::" + payload.ParameterType.Namespace + "." + payload.ParameterType.Name;

        // params, so one message and a batch are the same call. A batched source delivering one and
        // delivering ten reach the same handler the same way, and a separate single-message overload
        // would give a test two ways to say one thing.
        return $@"
            public global::System.Threading.Tasks.Task {methodName}(params {type}[] messages) =>
                _invoke(messages, {route});
";
    }

    /// <summary>
    /// A source name as a C# identifier: <c>orders-new</c> becomes <c>OrdersNew</c>.
    /// </summary>
    /// <remarks>
    /// Every separator is a word boundary and anything that is not a letter or a digit is dropped.
    /// A name starting with a digit gets a leading underscore, because a queue may legally be called
    /// <c>2024-archive</c> and a method may not.
    /// </remarks>
    internal static string Identifier(string source) {
        var builder = new StringBuilder(source.Length);
        var capitalise = true;

        foreach (var character in source) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(capitalise ? char.ToUpperInvariant(character) : character);
                capitalise = false;
            }
            else {
                capitalise = true;
            }
        }

        if (builder.Length == 0) {
            return "Root";
        }

        return char.IsDigit(builder[0]) ? "_" + builder : builder.ToString();
    }
}
