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
/// <b>The constructor names a delegate rather than taking a <c>Func</c> of the same shape.</b> A
/// harness finds a façade by looking for that constructor, and a structural match would also match
/// anything else with the same shape - so the delegate's identity is what stops somebody else's
/// type being handed a delegate it never asked for. It lives in Hardened.Requests.Abstract, which
/// every application already references, so a façade compiled into one adds no dependency; a
/// testing package here would have put test scaffolding in every published build's graph.
/// </para>
/// <para>
/// Written as text rather than through CSharpAuthor. The shape is fixed and small, and the two
/// things it needs - an array parameter and <c>params</c> - are the two the emitter has no vocabulary
/// for, so building it through the emitter would be more code that says less.
/// </para>
/// </remarks>
public static class TriggerFacadeGenerator {

    /// <summary>The kinds that get a façade, what a test calls to reach one, and what to call it.</summary>
    /// <remarks>
    /// <c>INVOKE</c> is here and shaped differently, and the difference is the reason it is worth
    /// having: a direct invocation answers. Its methods return what the handler returns and take
    /// one message rather than a batch, because there is a caller waiting and nothing to fan out.
    /// </remarks>
    private static readonly (string Scheme, string Facade, string Noun)[] Kinds = {
        ("QUEUE", "Queues", "queue"),
        ("TOPIC", "Topics", "topic"),
        ("TIMER", "Timers", "timer"),
        ("INVOKE", "Invocations", "operation")
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

            methods.Append(
                scheme == "INVOKE"
                    ? Call(methodName, handler)
                    : Method(methodName, scheme, handler));
        }

        if (scheme == "INVOKE") {
            // A different delegate, because an invocation answers: the type it should come back as
            // goes in, and the value comes out. Still all BCL types, so a façade compiled into an
            // application references no testing package.
            return $@"        public class {name}
        {{
            private readonly global::Hardened.Requests.Abstract.Execution.TriggerCall _call;

            public {name}(global::Hardened.Requests.Abstract.Execution.TriggerCall call)
            {{
                _call = call;
            }}
{methods}        }}
";
        }

        return $@"        public class {name}
        {{
            private readonly global::Hardened.Requests.Abstract.Execution.TriggerSend _send;

            public {name}(global::Hardened.Requests.Abstract.Execution.TriggerSend send)
            {{
                _send = send;
            }}
{methods}        }}
";
    }

    /// <summary>
    /// One invocation, and what it answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns the handler's own return type, so a test reads the receipt rather than a stream it
    /// has to parse - which is what a caller of a direct invocation actually gets back, and the one
    /// thing no trigger façade can offer.
    /// </para>
    /// <para>
    /// The response type is passed to the delivery rather than deserialized here, because how a
    /// response comes back differs by delivery: through the pipeline it is the object the handler
    /// returned, through an envelope it is bytes that have to be read with the framework's own
    /// conventions. Generated code should not have to know which.
    /// </para>
    /// </remarks>
    private static string Call(string methodName, RequestHandlerModel handler) {
        var payload = handler.RequestParameterInformationList
            .FirstOrDefault(parameter => parameter.BindingType == ParameterBindType.Body);

        var argument = payload == null
            ? "new object()"
            : "message";

        var parameter = payload == null
            ? ""
            : "global::" + payload.ParameterType.Namespace + "." + payload.ParameterType.Name +
              " message";

        var returns = handler.ResponseInformation.ReturnType;
        var route = $"\"INVOKE\", \"{handler.Name.Path}\"";

        // A void handler arrives as System.Void rather than as no type at all, and System.Void
        // cannot be written in C# - Task<System.Void> is a compile error, not a task with nothing
        // in it.
        if (returns == null || (returns.Namespace == "System" && returns.Name == "Void")) {
            return $@"
            public async global::System.Threading.Tasks.Task {methodName}({parameter}) =>
                await _call({argument}, {route}, null);
";
        }

        var type = "global::" + returns.Namespace + "." + returns.Name;

        return $@"
            public async global::System.Threading.Tasks.Task<{type}> {methodName}({parameter}) =>
                ({type})(await _call({argument}, {route}, typeof({type})))!;
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
                _send(global::System.Array.Empty<object>(), {route});
";
        }

        var type = "global::" + payload.ParameterType.Namespace + "." + payload.ParameterType.Name;

        // params, so one message and a batch are the same call. A batched source delivering one and
        // delivering ten reach the same handler the same way, and a separate single-message overload
        // would give a test two ways to say one thing.
        return $@"
            public global::System.Threading.Tasks.Task {methodName}(params {type}[] messages) =>
                _send(messages, {route});
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
