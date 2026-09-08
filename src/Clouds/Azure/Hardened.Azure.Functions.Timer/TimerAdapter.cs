using System.Text;
using System.Text.Json;
using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.Timer;

/// <summary>
/// A scheduled invocation.
/// </summary>
/// <remarks>
/// <para>
/// Payload-shaped and never batched: the host fires the function once per occurrence. A throw is
/// rethrown, so a failed run is recorded as failed against the schedule rather than as a success
/// that did nothing.
/// </para>
/// <para>
/// <b>The schedule lives in an app setting, not in the code.</b> The generated function's binding
/// is <c>%Hardened:Timers:{name}%</c>, which the host resolves against its settings, so a
/// deployment decides when <c>[Timer("nightly")]</c> runs - as the attribute's documentation
/// promises - and the same handler runs on a different schedule in every environment.
/// </para>
/// <para>
/// <b>The shim binds the timer as a string, and this is why.</b> The host sends the timer's state
/// as JSON, and the extension offers a <c>TimerInfo</c> model of it. A handler that wants the
/// schedule status declares a type with those properties and binds the body, the way it would
/// bind any message; one that does not takes nothing. Binding the model here would put the
/// extension's shape between the host and the handler for no gain.
/// </para>
/// <para>
/// Routes as <c>TIMER /nightly</c>, from the shim: the host binds a timer function to one
/// schedule, so the function's identity is the route.
/// </para>
/// </remarks>
public sealed class TimerAdapter : ITriggerAdapter {
    /// <summary>The scheme a schedule routes under, which <c>[Timer]</c> declares.</summary>
    public const string TimerScheme = "TIMER";

    /// <summary>Whether the host considers this occurrence late, as the timer's own flag says.</summary>
    public const string PastDueHeader = "x-azure-timer-past-due";

    /// <summary>
    /// Whether the shim was generated for this family. A string on its own says nothing - three
    /// families bind one - so the scheme the shim carries is part of the check.
    /// </summary>
    public bool Handles(FunctionsTrigger trigger) =>
        trigger.Scheme == TimerScheme && trigger.Data is string;

    public IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context) {
        var timer = (string)trigger.Data;

        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            ["Content-Type"] = "application/json"
        };

        if (PastDue(timer) is { } pastDue) {
            headers[PastDueHeader] = pastDue ? "true" : "false";
        }

        return new FunctionsPayloadRequest(
            trigger.Scheme,
            trigger.Path,
            string.IsNullOrEmpty(timer)
                ? Stream.Null
                : new MemoryStream(Encoding.UTF8.GetBytes(timer), writable: false),
            headers);
    }

    /// <summary>
    /// The one fact worth lifting out of the timer's JSON into a header: a handler that skips
    /// late runs can read it without binding the body.
    /// </summary>
    private static bool? PastDue(string timer) {
        if (string.IsNullOrEmpty(timer)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(timer);

            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject()) {
                // The host writes IsPastDue; a test may write it camel-cased. Either is the flag.
                if (string.Equals(property.Name, "IsPastDue", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) {
                    return property.Value.GetBoolean();
                }
            }
        }
        catch (JsonException) {
            // Not JSON, which the host never sends; the body carries whatever arrived.
        }

        return null;
    }

    public IExecutionResponse CreateResponse(Stream output) => new FunctionsPayloadResponse(output);

    /// <summary>Rethrown, so the host records a failed run against the schedule.</summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    /// <summary>Nothing. A timer reads no response.</summary>
    public ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext) =>
        new((object?)null);
}
