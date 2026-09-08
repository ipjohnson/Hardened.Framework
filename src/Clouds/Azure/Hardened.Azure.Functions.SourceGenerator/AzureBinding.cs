using System.Text;
using CSharpAuthor;

namespace Hardened.Azure.Functions.SourceGenerator;

/// <summary>
/// How one neutral trigger becomes a worker binding: the attribute the shim carries, the type the
/// worker binds for it, and the binding the host is told about.
/// </summary>
/// <remarks>
/// <para>
/// This table is the one place the Azure line names an Azure binding for a neutral trigger, and it
/// is here rather than in the adapter packages because a generator can see no assembly but its
/// own. Each row is written against one adapter package: the parameter type is what that
/// adapter's <c>ITriggerAdapter.Handles</c> recognises, and a row with no adapter behind it would
/// generate a function the worker could load and never serve.
/// </para>
/// <para>
/// <b>The raw binding has to agree with the Worker SDK's build task.</b> The task scans the
/// compiled shim and writes <c>functions.metadata</c> from the same attribute; the host indexes
/// whichever description it is given, and the Azure integration fixture asserts the two are equal.
/// So the JSON below is the task's own shape: the parameter name, the direction, the type the
/// attribute's name lowers to, the attribute's constructor argument under its parameter name,
/// <c>cardinality</c> in place of <c>IsBatched</c>, and <c>supportsDeferredBinding</c> for a type
/// the extension's converter advertises. Key order is the task's too, so a diff reads cleanly.
/// </para>
/// </remarks>
internal sealed class AzureBinding {
    private AzureBinding(
        string scheme,
        string functionPrefix,
        ITypeDefinition attribute,
        ITypeDefinition parameterType,
        string parameterName,
        IReadOnlyList<KeyValuePair<string, string>> namedArguments,
        Func<string, string> rawBinding) {
        Scheme = scheme;
        FunctionPrefix = functionPrefix;
        Attribute = attribute;
        ParameterType = parameterType;
        ParameterName = parameterName;
        NamedArguments = namedArguments;
        _rawBinding = rawBinding;
    }

    private readonly Func<string, string> _rawBinding;

    /// <summary>The scheme the handler routes under, which is what selects a row.</summary>
    public string Scheme { get; }

    /// <summary>The first half of the function name: <c>Queue</c> in <c>Queue_orders</c>.</summary>
    public string FunctionPrefix { get; }

    /// <summary>The worker's binding attribute, written on the shim's trigger parameter.</summary>
    public ITypeDefinition Attribute { get; }

    /// <summary>What the worker binds for the shim, and what the adapter recognises.</summary>
    public ITypeDefinition ParameterType { get; }

    /// <summary>The trigger parameter's name, which is also the binding's name in the metadata.</summary>
    public string ParameterName { get; }

    /// <summary>The attribute's named arguments, as C# text: <c>IsBatched = true</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> NamedArguments { get; }

    /// <summary>The binding as the host is told it, for the source named by the handler.</summary>
    public string RawBinding(string source) => _rawBinding(source);

    /// <summary>
    /// <c>[Queue("orders")]</c>: a batched Service Bus trigger on the queue, bound as
    /// <c>ServiceBusReceivedMessage[]</c> for <c>Hardened.Azure.Functions.ServiceBus</c>.
    /// </summary>
    /// <remarks>
    /// No <c>Connection</c>, so the host reads <c>AzureWebJobsServiceBus</c>, its default setting
    /// for the extension. A module property naming another setting is Phase 2, with the diagnostic
    /// for a binding whose setting the module did not supply.
    /// </remarks>
    public static readonly AzureBinding Queue = new AzureBinding(
        "QUEUE",
        "Queue",
        TypeDefinition.Get("Microsoft.Azure.Functions.Worker", "ServiceBusTriggerAttribute"),
        TypeDefinition.Get("Azure.Messaging.ServiceBus", "ServiceBusReceivedMessage", isArray: true),
        "messages",
        new[] { new KeyValuePair<string, string>("IsBatched", "true") },
        queue =>
            "{\"name\":\"messages\",\"direction\":\"In\",\"type\":\"serviceBusTrigger\"," +
            "\"queueName\":\"" + JsonEscape(queue) + "\",\"cardinality\":\"Many\"," +
            "\"properties\":{\"supportsDeferredBinding\":\"True\"}}");

    /// <summary>Every trigger this generator can write a function for.</summary>
    public static readonly IReadOnlyList<AzureBinding> All = new[] { Queue };

    public static AzureBinding? For(string scheme) {
        foreach (var binding in All) {
            if (binding.Scheme == scheme) {
                return binding;
            }
        }

        return null;
    }

    /// <summary>
    /// The characters a JSON string cannot carry bare. A Service Bus entity name allows none of
    /// them, and the escape is here so a name from another source cannot break the metadata.
    /// </summary>
    private static string JsonEscape(string value) {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value) {
            switch (character) {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character < ' ') {
                        builder.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
