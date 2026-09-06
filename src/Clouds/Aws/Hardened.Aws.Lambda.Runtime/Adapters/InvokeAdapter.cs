using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// A direct invocation: whatever JSON the caller chose, with no AWS envelope around it.
/// </summary>
/// <remarks>
/// <para>
/// The fallback, and the only adapter that recognises everything. <see cref="Handles"/> always
/// answers true, so it is registered last and reached when no envelope matched. That is not a
/// weakness of the peek: detection identifies AWS's own event shapes, and a payload that is none of
/// them is by definition the application's own.
/// </para>
/// <para>
/// <b>It does not parse the payload, and that is the point of D3.</b> The raw stream becomes the
/// request body and decoding is the binder's job further down, against the handler's own parameter
/// type. If the executor deserialised first and handed an adapter a typed event, this adapter would
/// have nothing to receive - and every other adapter would be forced into a payload type it may not
/// want.
/// </para>
/// </remarks>
public sealed class InvokeAdapter : IPayloadAdapter {
    /// <summary>
    /// The scheme this adapter routes under. One message, a response that is the payload, and no
    /// HTTP semantics.
    /// </summary>
    public const string Scheme = "INVOKE";

    /// <summary>
    /// The envelope field naming the operation to run, when the caller sets one.
    /// </summary>
    /// <remarks>
    /// A function with one operation needs nothing here and routes on its own name. A function with
    /// several needs a way to say which, and this is the same mechanism a CloudWatch widget already
    /// uses through its own <c>route</c> field - one function, many operations, selected by a field
    /// the caller sets.
    /// </remarks>
    public const string OperationField = "operation";

    /// <summary>Always. Nothing else claimed the payload, so it belongs to the application.</summary>
    public bool Handles(ReadOnlySpan<byte> head) => true;

    public IExecutionRequest CreateRequest(Stream payload, ILambdaContext context) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        // The client context's custom values, which is the only header-like channel a direct
        // invocation has. An SDK caller sets them; an event source sets none.
        var custom = context.ClientContext?.Custom;

        if (custom != null) {
            foreach (var pair in custom) {
                headers[pair.Key] = pair.Value;
            }
        }

        return new LambdaPayloadRequest(Scheme, "/" + context.FunctionName, payload, headers);
    }

    public IExecutionResponse CreateResponse(Stream output) =>
        new LambdaPayloadResponse(output);

    /// <summary>
    /// Nothing. The handler's return value was serialised into the output stream by the IO filter
    /// on the way back out, which is what the caller receives.
    /// </summary>
    public ValueTask WriteResponse(IExecutionContext context, Stream output) => default;
}
