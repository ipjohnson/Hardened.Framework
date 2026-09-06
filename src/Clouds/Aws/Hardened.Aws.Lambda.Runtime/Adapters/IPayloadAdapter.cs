using Amazon.Lambda.Core;
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
/// <b>The adapter owns deserialization, not the executor.</b> D3. If the executor deserialized
/// first and handed over a typed event, every adapter would be forced into a payload type it may
/// not want - and the direct-invoke adapter wants none at all, because the raw stream is the body
/// and decoding is the binder's job further down. So the adapter takes the stream. Each one brings
/// its own <c>JsonTypeInfo</c>, which is also what keeps ahead-of-time publishing honest: no
/// adapter can reach a serializer the application did not declare.
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
    /// Whether this adapter recognises the invocation, decided from the head of the payload rather
    /// than by parsing all of it.
    /// </summary>
    /// <remarks>
    /// AWS's own event shapes carry the discriminator: <c>Records[0].eventSource</c> separates
    /// <c>aws:sqs</c> from <c>aws:dynamodb</c>, <c>requestContext.http</c> means API Gateway v2,
    /// and an invocation matching nothing is a direct invoke. A function with one source registers
    /// its adapter alone and never pays for the peek.
    /// </remarks>
    bool Handles(ReadOnlySpan<byte> head);

    /// <summary>
    /// Builds the request. <paramref name="payload"/> is the invocation's stream, positioned at the
    /// start; the adapter reads as much of it as its own shape needs and no more.
    /// </summary>
    IExecutionRequest CreateRequest(Stream payload, ILambdaContext context);

    /// <summary>
    /// Builds the response the request will be answered into.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreateRequest"/> because the two are not always symmetric: a
    /// buffered gateway response collects into a pooled stream and is serialised at the end, and a
    /// streamed one opens the Lambda response stream at its first body byte.
    /// </remarks>
    IExecutionResponse CreateResponse(Stream output);

    /// <summary>
    /// Writes what the runtime expects, once the chain has finished.
    /// </summary>
    /// <remarks>
    /// A gateway adapter serialises status, headers and body into the proxy response shape here. A
    /// direct-invoke adapter has already written the handler's return value and does nothing. A
    /// batched source writes its per-record failure report, which is why this takes the context
    /// rather than only the response.
    /// </remarks>
    ValueTask WriteResponse(IExecutionContext context, Stream output);
}
