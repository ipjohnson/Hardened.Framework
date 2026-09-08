using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.Invoke;

/// <summary>
/// A direct invocation over HTTP: the operation in the URL, the caller's payload as the body.
/// </summary>
/// <remarks>
/// <para>
/// Cloud Run has no invoke API of its own; a service is invoked over HTTP, so a
/// <c>[HardenedFunction]</c> is reached by a POST to <c>/_triggers/invoke/{operation}</c>. The body
/// is handed on untouched - decoding it against the handler's parameter type is the binder's job,
/// as it is on Lambda - and every header of the delivery is kept, which is where a caller's
/// authorization token is.
/// </para>
/// <para>
/// Routes as <c>INVOKE /place-order</c>. The handler's return value is serialized into the
/// response by the pipeline, exactly as a web handler's is, so the caller reads it as the body
/// and reads a failure as the status; that is the one family on Cloud Run whose caller is waiting.
/// </para>
/// </remarks>
public sealed class InvokeEnvelope : ITriggerEnvelope {
    /// <summary>The scheme a direct invocation routes under.</summary>
    public const string InvokeScheme = "INVOKE";

    /// <summary>Where an invocation names its operation by default.</summary>
    public const string DefaultPrefix = "/_triggers/invoke/";

    public InvokeEnvelope(string prefix) {
        Prefix = TriggerHeaders.Prefix(prefix);
    }

    /// <summary>The path the operation is read under, rooted and ending in a slash.</summary>
    public string Prefix { get; }

    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(TriggerHeaders.Under(request.Path, Prefix));

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        var operation = TriggerHeaders.Under(request.Path, Prefix);

        if (string.IsNullOrEmpty(operation)) {
            return null;
        }

        return new CloudRunTriggerRequest(
            InvokeScheme, "/" + operation, payload.AsStream(), TriggerHeaders.Copy(request.Headers), request);
    }
}
