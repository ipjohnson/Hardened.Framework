using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Aws.Lambda.Invoke;

/// <summary>
/// A direct invocation: whatever JSON the caller chose, with no AWS envelope around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own function, so it never competes with an event adapter.</b> A direct invocation is the
/// one payload that does not describe itself: the caller sends whatever they like, and a field that
/// happens to be named <c>Records</c> or <c>requestContext</c> is theirs rather than AWS's. Nothing
/// can tell those apart by inspection, which is why the family split puts invoke in a function of
/// its own instead of asking a detector to guess.
/// </para>
/// <para>
/// <b>It does not parse the payload, and that is the point of D3.</b> The raw bytes become the
/// request body and decoding is the binder's job further down, against the handler's own parameter
/// type - so an invoke-only function never triggers <see cref="LambdaPayload.Json"/> and never
/// parses. If the executor deserialised first and handed an adapter a typed event, this adapter
/// would have nothing to receive.
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

    /// <summary>
    /// Always, and never asked. The invoke family holds this adapter alone, so there is nothing to
    /// discriminate against - and a caller's payload is the application's own by definition.
    /// </summary>
    public bool Handles(JsonElement payload) => true;

    public IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        // The client context's custom values, which is the only header-like channel a direct
        // invocation has. An SDK caller sets them; an event source sets none.
        var custom = context.ClientContext?.Custom;

        if (custom != null) {
            foreach (var pair in custom) {
                headers[pair.Key] = pair.Value;
            }
        }

        return new LambdaPayloadRequest(
            Scheme, "/" + context.FunctionName, payload.AsStream(), headers);
    }

    /// <summary>
    /// Rethrown. A direct invocation's caller reads a failure as a FunctionError with the exception
    /// on it, which is the AWS-native answer and more than a synthesised payload could carry.
    /// </summary>
    public HostFailurePolicy FailurePolicy => HostFailurePolicy.Rethrow;

    public IExecutionResponse CreateResponse(Stream output) =>
        new LambdaPayloadResponse(output);

    /// <summary>
    /// The body, which for this family is the whole answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The IO filter serialises the handler's return value into the response body on the way back
    /// out. Every other adapter then wraps that body in something - a proxy response, a batch
    /// failure report - and this one does not: a direct invocation's caller receives exactly what
    /// the handler returned. So the body is copied across rather than written into.
    /// </para>
    /// <para>
    /// It used to do nothing at all, on the reading that the IO filter had already written "the
    /// output stream". It had not: the body a response accumulates into and the stream the runtime
    /// sends back are two streams, and every direct invocation answered empty.
    /// </para>
    /// </remarks>
    public async ValueTask WriteResponse(IExecutionContext context, Stream output) {
        var body = context.Response.Body;

        if (body.CanSeek) {
            body.Position = 0;
        }

        await body.CopyToAsync(output);
    }
}
