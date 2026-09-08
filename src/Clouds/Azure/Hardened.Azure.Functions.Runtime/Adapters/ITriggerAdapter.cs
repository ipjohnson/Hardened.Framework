using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Azure.Functions.Worker;

namespace Hardened.Azure.Functions.Runtime.Adapters;

/// <summary>
/// Turns what the worker bound for one function into an <see cref="IExecutionRequest"/>, and the
/// answer back into what the host expects.
/// </summary>
/// <remarks>
/// <para>
/// The seam the Azure line rests on, shaped like <c>IPayloadAdapter</c> on Lambda: recognise, bind,
/// write back, and a <see cref="HostFailurePolicy"/>. Everything between an adapter and the
/// handler is <c>IRequestExecutor</c> and the pipeline, which are shared and which no adapter may
/// reimplement.
/// </para>
/// <para>
/// <b>Recognition is a type check, not a parse.</b> The Functions host invokes one function per
/// trigger and the generated shim knows which adapter family its binding attribute belongs to,
/// so what arrives is already the adapter's own type - <c>ServiceBusReceivedMessage[]</c>, an
/// event batch, a timer. <see cref="Handles"/> exists so a worker serving several families can
/// hand each invocation to the adapter whose type it carries, and so a shim generated for a
/// family whose adapter is not registered fails by name rather than by cast.
/// </para>
/// <para>
/// <b>Every adapter produces a route.</b> The shim carries it: <c>QUEUE /orders</c> is what the
/// pipeline dispatches on, through the same table as <c>GET /orders/{id}</c>.
/// </para>
/// </remarks>
public interface ITriggerAdapter {
    /// <summary>Whether this adapter binds the trigger data the shim received.</summary>
    bool Handles(FunctionsTrigger trigger);

    /// <summary>Builds the request.</summary>
    /// <remarks>
    /// Takes the worker's <see cref="FunctionContext"/> as well as the trigger, because some of
    /// what a source carries arrives as binding data rather than as the bound value - the trigger
    /// metadata the host sends beside a batch - and an adapter that needs it should not have to
    /// find it another way.
    /// </remarks>
    IExecutionRequest CreateRequest(FunctionsTrigger trigger, FunctionContext context);

    /// <summary>Builds the response the request will be answered into.</summary>
    IExecutionResponse CreateResponse(Stream output);

    /// <summary>
    /// What the host does when the chain fails.
    /// </summary>
    /// <remarks>
    /// A payload-shaped adapter rethrows, because failing the invocation <em>is</em> the answer:
    /// it is what makes the Service Bus extension abandon the batch so the queue redelivers, and
    /// what a timer records as a failed run. A web-shaped adapter answers 500, because a caller is
    /// waiting on a connection.
    /// </remarks>
    HostFailurePolicy FailurePolicy { get; }

    /// <summary>
    /// Writes what the host expects, once the chain has finished, and returns what the function
    /// answers the host with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The HTTP adapter builds the <c>HttpResponseData</c> here and returns it, because that is the
    /// one family whose function has a return value the host reads. A batched source settles or
    /// reports what its request recorded and returns null, which is why this takes the execution
    /// context rather than only the response, and the worker's context, which is where the
    /// settlement actions live.
    /// </para>
    /// <para>
    /// <c>object</c> rather than a type parameter, because the invocation handler holds every
    /// adapter behind one interface and the generated shim, which knows the family, is what casts.
    /// </para>
    /// </remarks>
    ValueTask<object?> WriteResponse(IExecutionContext context, FunctionContext functionContext);
}
