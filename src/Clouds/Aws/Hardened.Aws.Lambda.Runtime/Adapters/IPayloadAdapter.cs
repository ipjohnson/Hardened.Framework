using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Aws.Lambda.Runtime.Adapters;

/// <summary>
/// Turns one Lambda invocation into an <see cref="IExecutionRequest"/>, and the answer back into
/// what the runtime expects.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam the whole design rests on. A host differs from every other host in three
/// decisions: how its payload becomes a request, how a response becomes its payload, and who owns
/// the invocation loop. The first two are this interface; the third is the bootstrap. Everything
/// between them is <c>IRequestExecutor</c> and the pipeline, which are shared and which no adapter
/// may reimplement.
/// </para>
/// <para>
/// <b>An adapter recognises a payload and binds a payload, and those are two different reads.</b>
/// Recognising is a property lookup on an already-parsed document, cheap enough to ask every
/// candidate. Binding is a typed deserialize against the adapter's own event, which only the winner
/// pays for. Keeping them apart is what lets one function serve several sources without every
/// adapter deserializing the payload to find out it was not theirs.
/// </para>
/// <para>
/// <b>Every adapter produces a route.</b> An adapter has to put the payload somewhere, and once it
/// also has to choose a path, one executable serving many event sources stops being a feature and
/// becomes the ordinary case - dispatching a path to a handler is the thing the framework already
/// does best. <c>QUEUE /orders-new</c> and <c>GET /orders/{id}</c> are the same mechanism.
/// </para>
/// <para>
/// <b>Two request shapes, not one per source.</b> D7. A web-shaped request carries a path, query,
/// headers, cookies and a body; a payload-shaped one carries a record. Which an adapter produces is
/// about what the handler sees rather than how the bytes arrived, so a CloudWatch widget is
/// web-shaped despite arriving as a direct invoke. The shape decides which conformance profile the
/// adapter enrols in and whether a throw is answered or rethrown.
/// </para>
/// </remarks>
public interface IPayloadAdapter {
    /// <summary>
    /// Whether this adapter recognises the payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only ever called where a function serves several sources, which after the family split means
    /// inside the event family: SQS, SNS, streams and timers can share a function because they
    /// share a failure policy, a shape and a timeout profile. HTTP is its own function and direct
    /// invoke is its own function, so neither ever discriminates.
    /// </para>
    /// <para>
    /// That is what makes this cheap and safe. Every candidate is a documented schema, they are
    /// mutually exclusive, and a payload arriving at that function is one of the sources the
    /// deployment wired - so matching none of them is an error worth raising rather than a guess to
    /// fall back from. A property lookup on an already-parsed document is the whole of the check.
    /// </para>
    /// </remarks>
    bool Handles(JsonElement payload);

    /// <summary>
    /// Builds the request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes the payload rather than a stream so an adapter can choose. An event adapter binds
    /// <see cref="LambdaPayload.Raw"/> to its own type; the direct-invoke adapter hands the same
    /// bytes on as the request body and never parses at all, because decoding them is the binder's
    /// job further down, against the handler's own parameter type.
    /// </para>
    /// <para>
    /// <b>Bind from <see cref="LambdaPayload.Raw"/>, not from the element.</b>
    /// <c>JsonElement.Deserialize</c> does not read an element in place: it writes the element back
    /// out to a pooled buffer and parses that, so it costs a re-encode on top of the parse. Binding
    /// from the bytes also leaves the result owning its own strings, which is what keeps the
    /// document's lifetime a concern of <see cref="Handles"/> alone.
    /// </para>
    /// </remarks>
    IExecutionRequest CreateRequest(LambdaPayload payload, ILambdaContext context);

    /// <summary>
    /// Builds the response the request will be answered into.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreateRequest"/> because the two are not always symmetric: a
    /// buffered gateway response collects into a stream and is written at the end, and a streamed
    /// one opens the Lambda response stream at its first body byte.
    /// </remarks>
    IExecutionResponse CreateResponse(Stream output);

    /// <summary>
    /// What the host does when the chain fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of the shape decision. A web-shaped adapter answers 500, because the caller is
    /// on the other end of an HTTP connection and a failed invocation gives them a 502 with nothing
    /// in it - no status the application chose, no body, no correlation id.
    /// </para>
    /// <para>
    /// A payload-shaped adapter rethrows, because failing the invocation <em>is</em> the answer. It
    /// is what returns a message to its queue, what makes SNS redeliver, what a scheduled rule
    /// records as a failed run, and what an SDK caller reads as a FunctionError. An event adapter
    /// that answered instead would tell AWS every message was handled.
    /// </para>
    /// </remarks>
    HostFailurePolicy FailurePolicy { get; }

    /// <summary>
    /// Writes what the runtime expects, once the chain has finished.
    /// </summary>
    /// <remarks>
    /// A gateway adapter writes status, headers and body into the proxy response shape here. A
    /// direct-invoke adapter has already written the handler's return value and does nothing. A
    /// batched source writes its per-record failure report, which is why this takes the context
    /// rather than only the response.
    /// </remarks>
    ValueTask WriteResponse(IExecutionContext context, Stream output);
}
